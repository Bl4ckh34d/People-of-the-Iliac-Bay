using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PeopleOfTheIliacBay.Core;
static class Program
{
    static int Main()
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root!=null && !File.Exists(Path.Combine(root.FullName,"mod/Scripts/NativeNpcConfiscation.cs")))root=root.Parent;
        string Methods(string file,params string[] names)=>string.Join(Environment.NewLine,CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName,file)))
            .GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Where(value=>names.Contains(value.Identifier.ValueText)).Select(value=>value.ToFullString()));
        var fixture=CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName,"tests/MarketCommerce.Regression/Fixture.cs"))).GetCompilationUnitRoot();
        fixture=fixture.RemoveNode(fixture.Members.OfType<ClassDeclarationSyntax>().Single(value=>value.Identifier.ValueText=="Fixture"),SyntaxRemoveOptions.KeepNoTrivia);
        string bridge=Methods("mod/Scripts/NativeNpcInventoryBridge.cs","CheckBinding","SameItems","Execute","SplitExactStack","TryCapture")+
            Methods("mod/Scripts/NativeNpcInventoryBridge.Confiscation.cs","TryConfiscateExact");
        var operation=CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName,"mod/Scripts/NativeNpcConfiscation.cs"))).GetRoot()
            .DescendantNodes().OfType<ClassDeclarationSyntax>().Select(value=>value.ToFullString());
        string source="using System.Globalization;\n"+fixture.ToFullString().Replace("// BRIDGE_METHODS",bridge)
            .Replace("public ItemGroups ItemGroup;","public int EquipSlot;public ItemGroups ItemGroup;")
            .Replace("public Vector3 position;}","public Vector3 position;public Transform Parent;public bool IsChildOf(Transform root)=>Parent==root;}")+
            string.Join(Environment.NewLine,operation)+File.ReadAllText(Path.Combine(root.FullName,"tests/NpcConfiscation.Regression/Fixture.cs"));
        var refs=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(path=>MetadataReference.CreateFromFile(path)).ToList();
        refs.Add(MetadataReference.CreateFromFile(typeof(WorldStateStore).Assembly.Location));
        var compilation=CSharpCompilation.Create("NpcConfiscationFixture",new[]{CSharpSyntaxTree.ParseText(source)},refs,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output=new MemoryStream();var result=compilation.Emit(output);
        if(!result.Success)throw new InvalidOperationException(string.Join(Environment.NewLine,result.Diagnostics));
        return (int)Assembly.Load(output.ToArray()).GetType("Fixture").GetMethod("Run").Invoke(null,null);
    }
}
