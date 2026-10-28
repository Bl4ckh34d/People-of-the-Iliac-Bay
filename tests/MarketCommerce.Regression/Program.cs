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
        while(root!=null && !File.Exists(Path.Combine(root.FullName,"mod/Scripts/NativeVendorTradeAdapter.cs")))root=root.Parent;
        if(root==null)throw new InvalidOperationException("Source root missing.");
        string Methods(string file,params string[] names)=>string.Join(Environment.NewLine,
            CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName,file))).GetRoot()
            .DescendantNodes().OfType<MethodDeclarationSyntax>().Where(m=>names.Contains(m.Identifier.ValueText)).Select(m=>m.ToFullString()));
        string bridge=Methods("mod/Scripts/NativeNpcInventoryBridge.cs","TryPurchaseCreatedItem","TryPurchaseOwnedStock","TryTransfer","CheckBinding","SameItems","Execute","SplitExactStack");
        string commercial="mod/Scripts/NativeNpcInventoryBridge.CommercialStock.cs";
        if(File.Exists(Path.Combine(root.FullName,commercial)))bridge+=Methods(commercial,"TryPurchaseCommercialStock");
        var available=CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName,"mod/Scripts/NativeMarketCatalog.cs"))).GetRoot()
            .DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c=>c.Identifier.ValueText=="NativeMarketPost")
            .Members.OfType<PropertyDeclarationSyntax>().Single(p=>p.Identifier.ValueText=="Available").ToFullString();
        string source=File.ReadAllText(Path.Combine(root.FullName,"tests/MarketCommerce.Regression/Fixture.cs"))
            .Replace("// BRIDGE_METHODS",bridge)
            .Replace("// SALE_ITEM_METHOD",Methods("mod/Scripts/NativeMarketStock.cs","IsSaleItem"))
            .Replace("// AVAILABILITY_PROPERTY",available)
            .Replace("// ADAPTER_METHOD",Methods("mod/Scripts/NativeVendorTradeAdapter.cs","TryNpcPurchase"));
        var references=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(p=>MetadataReference.CreateFromFile(p)).ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(WorldStateStore).Assembly.Location));
        var compilation=CSharpCompilation.Create("MarketCommerceFixture",new[]{CSharpSyntaxTree.ParseText(source)},references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream=new MemoryStream();var result=compilation.Emit(stream);
        if(!result.Success)throw new InvalidOperationException(string.Join(Environment.NewLine,result.Diagnostics));
        return (int)Assembly.Load(stream.ToArray()).GetType("Fixture").GetMethod("Run").Invoke(null,null);
    }
}
