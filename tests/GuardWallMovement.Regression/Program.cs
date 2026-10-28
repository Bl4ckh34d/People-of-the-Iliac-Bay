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
        while(root!=null && !File.Exists(Path.Combine(root.FullName,"mod/Scripts/GuardActor.WallMovement.cs")))root=root.Parent;
        if(root==null)throw new InvalidOperationException("Source root missing.");
        string Method(string path,string name)=>CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>().Single(value=>value.Identifier.ValueText==name).ToFullString();
        string motor=Path.Combine(root.Parent.FullName,"daggerfall-unity/Assets/Scripts/Game/EnemyMotor.cs");
        var obstacle=CSharpSyntaxTree.ParseText(Method(motor,"ObstacleCheck")).GetRoot();
        var radius=obstacle.DescendantNodes().OfType<LocalDeclarationStatementSyntax>()
            .Single(value=>value.Declaration.Variables.Any(variable=>variable.Identifier.ValueText=="checkDistance"));
        string source=File.ReadAllText(Path.Combine(root.FullName,"tests/GuardWallMovement.Regression/Fixture.cs"))
            .Replace("// RETURN_METHOD",Method(Path.Combine(root.FullName,"mod/Scripts/GuardActor.WallMovement.cs"),"ReturnToWallPost"))
            .Replace("// FALL_METHOD",Method(motor,"FallCheck"))
            .Replace("// NATIVE_RADIUS",radius.ToFullString());
        var refs=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(path=>MetadataReference.CreateFromFile(path));
        var compilation=CSharpCompilation.Create("GuardWallMovementFixture",new[]{CSharpSyntaxTree.ParseText(source)},refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output=new MemoryStream();var result=compilation.Emit(output);
        if(!result.Success)throw new InvalidOperationException(string.Join(Environment.NewLine,result.Diagnostics));
        return (int)Assembly.Load(output.ToArray()).GetType("Fixture").GetMethod("Run").Invoke(null,null);
    }
}
