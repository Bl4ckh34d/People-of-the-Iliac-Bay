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
        while(root!=null && !File.Exists(Path.Combine(root.FullName,"mod/Scripts/GuardWallWatch.cs")))root=root.Parent;
        if(root==null)throw new InvalidOperationException("Source root missing.");
        string Method(string file,string name)=>CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName,file)))
            .GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(value=>value.Identifier.ValueText==name).ToFullString();
        var place=CSharpSyntaxTree.ParseText(Method("mod/Scripts/GuardActor.cs","Place")).GetRoot();
        var pose=place.DescendantNodes().OfType<IfStatementSyntax>().Single(value=>value.Statement is ExpressionStatementSyntax statement &&
            statement.Expression is InvocationExpressionSyntax call && call.ArgumentList.Arguments.Count==1 &&
            call.ArgumentList.Arguments[0].Expression.ToString()=="refugeState");
        var fixture=File.ReadAllText(Path.Combine(root.FullName,"tests/WallGuardLaw.Regression/Fixture.cs"))
            .Replace("// WATCH_METHOD",Method("mod/Scripts/GuardWallWatch.cs","DefendWallWatch"))
            .Replace("// HIT_METHOD",Method("mod/Scripts/GuardRangedDefense.cs","ObserveGuardAttack"))
            .Replace("// ATTACK_METHOD",Method("mod/Scripts/GuardArcheryRuntime.cs","DefendWallPost"))
            .Replace("// OFFER_METHOD",Method("mod/Scripts/GuardActor.WallLaw.cs","OfferWallArrest"))
            .Replace("// POSE_STATEMENT",pose.ToFullString())
            .Replace("RETURN_OBSERVER",CSharpSyntaxTree.ParseText(Method("mod/Scripts/GuardShiftProbe.cs","VerifyWallReturn")).GetRoot()
                .DescendantNodes().OfType<AssignmentExpressionSyntax>().First(value=>value.Left.ToString()=="game.PlayerMotor.transform.position").Right.ToFullString())
            .Replace("SHOT_CONDITION",CSharpSyntaxTree.ParseText(Method("mod/Scripts/GuardArcheryRuntime.cs","ShootGuardArrow")).GetRoot()
                .DescendantNodes().OfType<VariableDeclaratorSyntax>().Single(value=>value.Identifier.ValueText=="canShoot").Initializer.Value.ToFullString())
            .Replace("// POLICY_METHOD",Method("src/PeopleOfTheIliacBay.Core/GuardShifts.cs","WallTrespassEnforced"));
        var references=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(p=>MetadataReference.CreateFromFile(p));
        var compilation=CSharpCompilation.Create("WallGuardFixture",new[]{CSharpSyntaxTree.ParseText(fixture)},references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output=new MemoryStream();var result=compilation.Emit(output);
        if(!result.Success)throw new InvalidOperationException(string.Join(Environment.NewLine,result.Diagnostics));
        return (int)Assembly.Load(output.ToArray()).GetType("Fixture").GetMethod("Run").Invoke(null,null);
    }
}
