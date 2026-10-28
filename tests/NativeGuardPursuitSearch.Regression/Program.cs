using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int Main()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root!=null && !File.Exists(Path.Combine(root.FullName,"mod/Scripts/GuardPursuitRecruitment.cs")))root=root.Parent;
        if(root==null)throw new InvalidOperationException("Source root missing.");
        string Methods(string file,params string[] names)=>string.Join(Environment.NewLine,
            CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName,file))).GetRoot().DescendantNodes()
                .OfType<MethodDeclarationSyntax>().Where(value=>names.Contains(value.Identifier.ValueText)).Select(value=>value.ToFullString()));
        string fixture=File.ReadAllText(Path.Combine(root.FullName,"tests/NativeGuardPursuitSearch.Regression/Fixture.cs"))
            .Replace("// PRODUCTION_METHODS",Methods("mod/Scripts/GuardPursuitRecruitment.cs","RecruitNearbyPursuitGuards")+
                Methods("mod/Scripts/GuardHaltResponse.cs","HearGuardHalt")+
                Methods("mod/Scripts/ExteriorStaticWitnesses.cs","CorpseSearchPatrol","CorpsePatrolLoop","HasPhysicalStaticWitness","CorpseSearchRouteLength","CorpseInquiryDestinations"));
        var references=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(p=>MetadataReference.CreateFromFile(p)).ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(WorldStateStore).Assembly.Location));
        var compilation=CSharpCompilation.Create("GuardFixture",new[]{CSharpSyntaxTree.ParseText(fixture)},references,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var result=new MemoryStream();var emitted=compilation.Emit(result);
        if(!emitted.Success)throw new InvalidOperationException(string.Join(Environment.NewLine,emitted.Diagnostics));
        return (int)Assembly.Load(result.ToArray()).GetType("Fixture").GetMethod("Run").Invoke(null,null);
    }
}
