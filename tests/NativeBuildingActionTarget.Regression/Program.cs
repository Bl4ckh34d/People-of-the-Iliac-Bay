using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

// Execute the production resolver against controlled native-door/terrain adapters.
// Roslyn extracts the method structurally; this is not a text-pattern assertion.
internal static class Program
{
    private static int Main()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "mod", "Scripts", "StreetPopulation.BuildingActions.cs")))
            root = root.Parent;
        if (root == null) throw new InvalidOperationException("Repository root not found.");
        string source = File.ReadAllText(Path.Combine(root.FullName, "mod", "Scripts", "StreetPopulation.BuildingActions.cs"));
        var method = CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(value => value.Identifier.ValueText == "ResolveBuildingAction");
        string fixture = File.ReadAllText(Path.Combine(root.FullName, "tests", "NativeBuildingActionTarget.Regression", "Fixture.cs"));
        fixture = fixture.Replace("// PRODUCTION_METHOD", method.ToFullString());
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("NativeDoorFixture", new[] { CSharpSyntaxTree.ParseText(fixture) }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var assembly = new MemoryStream();
        var emitted = compilation.Emit(assembly);
        if (!emitted.Success) throw new InvalidOperationException(string.Join(Environment.NewLine, emitted.Diagnostics));
        var fixtureType = Assembly.Load(assembly.ToArray()).GetType("Fixture");
        int failures = (int)fixtureType.GetMethod("Run").Invoke(null, null);
        failures += LifecycleChecks.Run();
        Console.WriteLine(failures == 0 ? "Native building target regression passed (6 resolver checks, 3 lifecycle scenarios)." : failures + " failures.");
        return failures == 0 ? 0 : 1;
    }
}
