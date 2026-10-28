using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PeopleOfTheIliacBay.Core;

public static class Program
{
    public static bool Accept(WorldStateStore store,NpcActionJob job,out string reason)
    {return PeopleOfTheIliacBay.Tests.ClassifiedAdmissionFixture.TryAcceptAction(store,job,true,true,true,out reason);}
    private static int Main()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName,"mod/Scripts/StreetPopulation.HealthRest.cs"))) root=root.Parent;
        if(root==null)throw new InvalidOperationException("Source root missing.");
        string Methods(string file, params string[] names) => string.Join(Environment.NewLine,
            CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName,file))).GetRoot()
                .DescendantNodes().OfType<MethodDeclarationSyntax>()
                .Where(value=>names.Contains(value.Identifier.ValueText)).Select(value=>value.ToFullString()));
        string fixture=File.ReadAllText(Path.Combine(root.FullName,"tests/NativeNpcSimplification.Regression/Fixture.cs"))
            .Replace("// PURCHASE_METHODS",Methods("mod/Scripts/NativeNpcInventoryBridge.cs",
                "TryPurchase","TryPurchaseCreatedItem","Execute","CheckBinding","SameItems","SplitExactStack")+
                Methods("mod/Scripts/NativeNpcShopPurchase.cs","TryCompleteActionPurchase"))
            .Replace("// REST_METHODS",Methods("mod/Scripts/StreetPopulation.HealthRest.cs",
                "IsHealthRestPoseAtTarget","UpdateHealthRestPose"));
        var references=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
            .Select(path=>MetadataReference.CreateFromFile(path)).ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(WorldStateStore).Assembly.Location));
        var compilation=CSharpCompilation.Create("SimplificationFixture",new[]{CSharpSyntaxTree.ParseText(fixture)},references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream=new MemoryStream();var result=compilation.Emit(stream);
        if(!result.Success)throw new InvalidOperationException(string.Join(Environment.NewLine,result.Diagnostics));
        return (int)Assembly.Load(stream.ToArray()).GetType("Fixture").GetMethod("Run").Invoke(null,null);
    }
}
