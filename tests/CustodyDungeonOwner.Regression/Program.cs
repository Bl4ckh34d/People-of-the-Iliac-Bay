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
        while(root!=null && !File.Exists(Path.Combine(root.FullName,"mod/Scripts/StreetPopulation.cs")))root=root.Parent;
        var tree=CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName,"mod/Scripts/StreetPopulation.cs"))).GetRoot();
        var body=tree.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(value=>value.Identifier.ValueText=="UpdatePopulation").Body;
        bool PauseGate(StatementSyntax statement)=>statement is IfStatementSyntax condition && condition.Condition.DescendantNodesAndSelf()
            .OfType<InvocationExpressionSyntax>().Any(value=>value.Expression is MemberAccessExpressionSyntax member && member.Name.Identifier.ValueText=="IsPlayingGame");
        string prefix=string.Join(Environment.NewLine,body.Statements.TakeWhile(statement=>!PauseGate(statement)));
        var custody=CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName,"mod/Scripts/StreetPopulation.NpcCustody.cs"))).GetRoot();
        var method=custody.DescendantNodes().OfType<MethodDeclarationSyntax>().SingleOrDefault(value=>value.Identifier.ValueText=="UpdateDungeonCustody");
        var guards=CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName,"mod/Scripts/GuardPopulation.cs"))).GetRoot();
        var hide=guards.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(value=>value.Identifier.ValueText=="HideViews");
        var native=CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName,"mod/Scripts/NativeNpcCustody.cs"))).GetRoot();
        var ownership=native.DescendantNodes().OfType<PropertyDeclarationSyntax>().Single(value=>value.Identifier.ValueText=="OwnsLoadedDungeon");
        var capture=native.DescendantNodes().OfType<MethodDeclarationSyntax>().Single(value=>value.Identifier.ValueText=="CaptureCustody");
        string capturePrefix=string.Join(Environment.NewLine,capture.Body.Statements.TakeWhile(statement=>!(statement is LocalDeclarationStatementSyntax local &&
            local.Declaration.Variables.Any(variable=>variable.Identifier.ValueText=="actual"))));
        var source=File.ReadAllText(Path.Combine(root.FullName,"tests/CustodyDungeonOwner.Regression/Fixture.cs"))
            .Replace("// ACTUAL_OWNER_PREFIX",prefix).Replace("// ACTUAL_DUNGEON_CUSTODY",method==null?
                "private void UpdateDungeonCustody(DaggerfallDungeon dungeon,double now){}":method.ToFullString())
            .Replace("// ACTUAL_HIDE",hide.ToFullString()).Replace("// ACTUAL_SESSION_OWNERSHIP",ownership.ToFullString())
            .Replace("// ACTUAL_CAPTURE_PREFIX",capturePrefix);
        var refs=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(path=>MetadataReference.CreateFromFile(path));
        var compilation=CSharpCompilation.Create("CustodyDungeonOwner",new[]{CSharpSyntaxTree.ParseText(source)},refs,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream=new MemoryStream();var result=compilation.Emit(stream);
        if(!result.Success)throw new InvalidOperationException(string.Join(Environment.NewLine,result.Diagnostics));
        return (int)Assembly.Load(stream.ToArray()).GetType("Fixture").GetMethod("Run").Invoke(null,null);
    }
}
