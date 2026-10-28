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
        while(root!=null&&!File.Exists(Path.Combine(root.FullName,"mod/Scripts/GuardPopulation.DialogueComplaints.cs")))root=root.Parent;
        if(root==null)throw new InvalidOperationException("Source root missing.");
        string Methods(string file,params string[] names)=>string.Join(Environment.NewLine,CSharpSyntaxTree.ParseText(
            File.ReadAllText(Path.Combine(root.FullName,file))).GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(m=>names.Contains(m.Identifier.ValueText)).Select(m=>m.ToFullString()));
        string source=File.ReadAllText(Path.Combine(root.FullName,"tests/ComplaintFeedback.Regression/Fixture.cs"))
            .Replace("// HUD_METHODS",Methods("mod/Scripts/PeopleOfTheIliacBayMod.ConversationKnowledge.cs","CanStartMobileConversation","CanStartStaticConversation"))
            .Replace("// GUARD_METHODS",Methods("mod/Scripts/GuardPopulation.DialogueComplaints.cs","TryReportConversationBoundaryAtGuard","CancelBoundaryReportConversation","TryFindNearestComplaintGuard"));
        var refs=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(p=>MetadataReference.CreateFromFile(p)).ToList();
        refs.Add(MetadataReference.CreateFromFile(typeof(ConversationAccess).Assembly.Location));
        var compilation=CSharpCompilation.Create("ComplaintFixture",new[]{CSharpSyntaxTree.ParseText(source)},refs,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var output=new MemoryStream();var emitted=compilation.Emit(output);
        if(!emitted.Success)throw new InvalidOperationException(string.Join(Environment.NewLine,emitted.Diagnostics));
        return (int)Assembly.Load(output.ToArray()).GetType("Fixture").GetMethod("Run").Invoke(null,null);
    }
}
