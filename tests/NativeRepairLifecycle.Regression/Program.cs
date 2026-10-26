using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.Tests;

// Execute production adapter methods and DFU ItemRepairData with a controlled
// scene/clock/item host. This proves source behavior, not Unity navigation.
public static class Program
{
    public static bool Accept(WorldStateStore store, NpcActionJob job, out string reason)
    { return ClassifiedAdmissionFixture.TryAcceptAction(store, job, true, true, true, out reason); }

    public static int Main()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "mod/Scripts/StreetPopulation.NativeRepair.cs"))) root = root.Parent;
        if (root == null) throw new InvalidOperationException("Repository root not found.");
        var syntax = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName, "mod/Scripts/StreetPopulation.NativeRepair.cs"))).GetRoot();
        string[] names = { "IsNativeRepairTarget", "RepairableOwnedNpcItem", "TryNativeRepairQuote", "CollectableNativeRepairItem", "ExecuteNativeRepairAction" };
        string methods = string.Join(Environment.NewLine, syntax.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(method => names.Contains(method.Identifier.ValueText)).Select(method => method.ToFullString()));
        var context = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName, "mod/Scripts/PeopleOfTheIliacBayMod.ContextTools.cs"))).GetRoot();
        var itemProjection = context.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
            .Single(value => value.Type.ToString() == "ConversationContextInventoryItem");
        string fixture = File.ReadAllText(Path.Combine(root.FullName, "tests/NativeRepairLifecycle.Regression/Fixture.cs"))
            .Replace("// PRODUCTION_METHODS", methods).Replace("PRODUCTION_PROJECTION", itemProjection.ToFullString());
        var owner = CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName, "mod/Scripts/NativeNpcInventoryOwner.cs"))).GetRoot();
        fixture = fixture.Replace("// PRODUCTION_WEARABLES", owner.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Single(method => method.Identifier.ValueText == "EquipOwnedWearables").ToFullString());
        var sources = new[] { CSharpSyntaxTree.ParseText(fixture), CSharpSyntaxTree.ParseText(File.ReadAllText(
            Path.Combine(root.Parent.FullName, "daggerfall-unity/Assets/Scripts/Game/Items/ItemRepairData.cs"))) }.ToList();
        string helper = Path.Combine(root.FullName, "mod/Scripts/NativeNpcRepairContext.cs");
        if (File.Exists(helper)) sources.Add(CSharpSyntaxTree.ParseText(File.ReadAllText(helper)));
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path)).ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(WorldStateStore).Assembly.Location));
        var compilation = CSharpCompilation.Create("RepairFixture", sources, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var result = new MemoryStream();
        var emitted = compilation.Emit(result);
        if (!emitted.Success) throw new InvalidOperationException(string.Join(Environment.NewLine, emitted.Diagnostics));
        return (int)Assembly.Load(result.ToArray()).GetType("PeopleOfTheIliacBay.DFU.StreetPopulation").GetMethod("Run").Invoke(null, null);
    }
}
