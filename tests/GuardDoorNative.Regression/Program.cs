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
    private static string Method(string path,string name)=>CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot()
        .DescendantNodes().OfType<MethodDeclarationSyntax>().Single(value=>value.Identifier.ValueText==name).ToFullString();
    private static string MovementAlarm(string path,string method)
    {
        var body=CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot().DescendantNodes()
            .OfType<MethodDeclarationSyntax>().Single(value=>value.Identifier.ValueText==method);
        var move=body.DescendantNodes().OfType<InvocationExpressionSyntax>().Single(value=>
            value.Expression is MemberAccessExpressionSyntax access && access.Expression.ToString()=="CivilianResponses" && access.Name.Identifier.ValueText=="Move");
        var block=move.Ancestors().OfType<BlockSyntax>().First();
        return block.Statements.OfType<LocalDeclarationStatementSyntax>().Single(value=>
            value.Declaration.Variables.Any(variable=>variable.Identifier.ValueText=="alarm")).ToFullString();
    }
    private static int Main()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root!=null && !Directory.Exists(Path.Combine(root.FullName,"mod","Scripts")))root=root.Parent;
        if(root==null)throw new Exception("Repository unavailable.");
        string guard=Path.Combine(root.FullName,"mod","Scripts","GuardDoorPursuit.cs");
        string fixture=File.ReadAllText(Path.Combine(root.FullName,"tests","GuardDoorNative.Regression","Fixture.cs"));
        fixture=fixture.Replace("// GUARD_METHODS",string.Join(Environment.NewLine,new[] {
            "PursuitDoorId","ObserveResidentBuildingEntry","ObserveResidentBuildingEntryCore","SeesDoorTarget","TryVisibleResidentPursuer","UpdateWitnessedDoorInterior"
        }.Select(name=>Method(guard,name))));
        fixture=fixture.Replace("// DOOR_GROUND",Method(Path.Combine(root.FullName,"mod","Scripts","StreetPopulation.Actions.Transitions.cs"),"FollowExteriorDoorGround"));
        fixture=fixture.Replace("// SHELTER_HOLD",Method(Path.Combine(root.FullName,"mod","Scripts","StreetPopulation.LawfulGuardFlight.cs"),"HoldLawfulGuardShelter"));
        string streets=Path.Combine(root.FullName,"mod","Scripts","StreetPopulation.cs");
        fixture=fixture.Replace("// EXTERIOR_ALARM",MovementAlarm(streets,"UpdatePopulation"));
        fixture=fixture.Replace("// INTERIOR_ALARM",MovementAlarm(streets,"UpdateInterior"));
        var owner=CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName,"mod","Scripts","StreetPopulation.LawfulGuardFlight.cs")))
            .GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().SingleOrDefault(value=>value.Identifier.ValueText=="AlarmForReaction");
        fixture=fixture.Replace("// REACTION_ALARM_HELPER",owner==null?"":owner.ToFullString());
        var references=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
            .Select(path=>MetadataReference.CreateFromFile(path)).ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(GuardRoster).Assembly.Location));
        var compilation=CSharpCompilation.Create("GuardDoorNativeFixture",new[]{CSharpSyntaxTree.ParseText(fixture)},references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output=new MemoryStream();var emitted=compilation.Emit(output);
        if(!emitted.Success)throw new Exception(string.Join(Environment.NewLine,emitted.Diagnostics));
        return (int)Assembly.Load(output.ToArray()).GetType("GuardPopulation").GetMethod("Run").Invoke(null,null);
    }
}
