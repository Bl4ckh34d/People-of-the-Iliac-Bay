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
    private static int Main(string[] args)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "mod", "Scripts", "StreetPopulation.BuildingActions.cs")))
            root = root.Parent;
        if (root == null) throw new InvalidOperationException("Repository root not found.");
        if(args.Contains("--arrival-only"))return RunObservedArrival(root.FullName);
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
        failures += RunTaskTiming(root.FullName);
        Console.WriteLine(failures == 0 ? "Native building target regression passed (6 resolver checks, 3 lifecycle scenarios, 4 runtime timing scenarios)." : failures + " failures.");
        return failures == 0 ? 0 : 1;
    }

    private static int RunObservedArrival(string root)
    {
        var method=CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,"mod/Scripts/StreetPopulation.Actions.cs")))
            .GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(value=>value.Identifier.ValueText=="ApplyNpcActionOverlay");
        var branch=method.DescendantNodes().OfType<IfStatementSyntax>().Single(value=>value.Condition.ToString()=="NpcActionExecutor.IsTerminal(job)");
        string fixture=@"using System;
public enum NpcTaskPhase {Ready,Running} public class Task {public NpcTaskPhase Phase;}
public class Receipt {public bool DestinationObserved;} public class Job {public string Id;public Receipt BuildingTransitionReceipt;}
public static class NpcActionExecutor {public static bool IsTerminal(Job job)=>true;}
public class Store {public Task Task;public Job Current;public Task ActiveNpcTask(string id)=>Task;public Job ActiveNpcAction(string id)=>Current;}
public class Resident {public string Id=""actual"";} public class Plan {public Resident Resident=new Resident();}
public class Fixture {
 Store worldStore=new Store();bool healthOwnsRoutine;int dispatched,returned;Job original;
 void AdvanceConversationTask(string id,object actor,double now,bool allow) {if(allow){dispatched++;worldStore.Current=new Job{Id=""next""};}}
 bool HoldNpcHealthCare(object plan,ref object actor,double now)=>true;
 bool ReturnActionToRoutine(object plan,object actor,Job job,object motion,double now){returned++;return true;}
 bool Execute(){var job=original;var plan=new Plan();object actor=new object(),motion=new object();double now=1;bool nativeOwnsMovement=false;
 // BRANCH
 return false;}
 public static int Run(){int failed=0;foreach(string scenario in new[]{""ready-observed"",""no-task"",""not-observed"",""care""}){
 var f=new Fixture();f.original=new Job{Id=""enter"",BuildingTransitionReceipt=new Receipt{DestinationObserved=scenario!=""not-observed""}};
 f.worldStore.Current=f.original;f.worldStore.Task=scenario==""no-task""?null:new Task{Phase=NpcTaskPhase.Ready};f.healthOwnsRoutine=scenario==""care"";
 f.Execute();bool pass=scenario==""ready-observed""?f.dispatched==1 && f.returned==0:scenario==""care""?f.dispatched==0 && f.returned==0:f.dispatched==0 && f.returned==1;
 Console.WriteLine((pass?""PASS "":""FAIL "")+scenario+"" dispatch=""+f.dispatched+"" routineReturn=""+f.returned);if(!pass)failed++;}return failed;}}
".Replace("// BRANCH",branch.ToFullString());
        var refs=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(path=>MetadataReference.CreateFromFile(path));
        var compilation=CSharpCompilation.Create("ObservedArrivalFixture",new[]{CSharpSyntaxTree.ParseText(fixture)},refs,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream=new MemoryStream();var result=compilation.Emit(stream);
        if(!result.Success)throw new InvalidOperationException(string.Join(Environment.NewLine,result.Diagnostics));
        int failures=(int)Assembly.Load(stream.ToArray()).GetType("Fixture").GetMethod("Run").Invoke(null,null);
        return failures==0?0:1;
    }

    private static int RunTaskTiming(string root)
    {
        string source = File.ReadAllText(Path.Combine(root, "mod", "Scripts", "StreetPopulation.Actions.Tasks.cs"));
        var methods = CSharpSyntaxTree.ParseText(source).GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>();
        string fixture = File.ReadAllText(Path.Combine(root, "tests", "NativeBuildingActionTarget.Regression", "TaskTimingFixture.cs"));
        foreach (string name in new[] { "TryAcceptConversationTaskCore", "DispatchConversationTaskStep" })
            fixture = fixture.Replace("// " + name, methods.Single(value => value.Identifier.ValueText == name).ToFullString());
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path)).ToList();
        references.Add(MetadataReference.CreateFromFile(typeof(Program).Assembly.Location));
        var compilation = CSharpCompilation.Create("TaskTimingFixture", new[] { CSharpSyntaxTree.ParseText(fixture) }, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var assembly = new MemoryStream();
        var emitted = compilation.Emit(assembly);
        if (!emitted.Success) throw new InvalidOperationException(string.Join(Environment.NewLine, emitted.Diagnostics));
        return (int)Assembly.Load(assembly.ToArray()).GetType("TaskTimingFixture.Fixture").GetMethod("Run").Invoke(null, null);
    }
}
