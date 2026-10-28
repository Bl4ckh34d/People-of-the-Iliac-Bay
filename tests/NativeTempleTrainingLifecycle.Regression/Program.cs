using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.Tests;

public static class Program
{
    public static bool Accept(WorldStateStore store, NpcActionJob job, out string reason)
    { return ClassifiedAdmissionFixture.TryAcceptAction(store, job, true, true, true, out reason); }

    public static int Main()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "mod/Scripts/NativeNpcTempleTraining.cs"))) root = root.Parent;
        if (root == null) throw new InvalidOperationException("Repository root not found.");
        var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName, "mod/Scripts/NativeNpcTempleTraining.cs"))).GetRoot();
        string[] names = { "TryExecuteStage", "ApplyNativeTraining" };
        string methods = string.Join(Environment.NewLine, syntax.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(method => names.Contains(method.Identifier.ValueText)).Select(method => method.ToFullString()));
        string fixture = File.ReadAllText(Path.Combine(root.FullName, "tests/NativeTempleTrainingLifecycle.Regression/Fixture.cs"))
            .Replace("// PRODUCTION_METHODS", methods);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path)).ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(WorldStateStore).Assembly.Location));
        var compilation = CSharpCompilation.Create("TrainingFixture", new[] { CSharpSyntaxTree.ParseText(fixture) }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var result = new MemoryStream();
        var emitted = compilation.Emit(result);
        if (!emitted.Success) throw new InvalidOperationException(string.Join(Environment.NewLine, emitted.Diagnostics));
        return (int)Assembly.Load(result.ToArray()).GetType("PeopleOfTheIliacBay.DFU.NativeNpcTempleTraining").GetMethod("Run").Invoke(null, null);
    }
}
