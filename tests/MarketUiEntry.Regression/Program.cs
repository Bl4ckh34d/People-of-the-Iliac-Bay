using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

static class Program
{
    static int Main(string[] args)
    {
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root!=null && !File.Exists(Path.Combine(root.FullName,"mod/Scripts/PeopleOfTheIliacBayMod.Dialogue.cs")))root=root.Parent;
        if(root==null)throw new InvalidOperationException("Source root missing.");
        if(args.Contains("--market-clock-only"))return VerifyMarketClock(root.FullName);
        if(args.Contains("--market-arrival-only"))return VerifyMarketArrival(root.FullName);
        if(args.Contains("--market-hooks-only"))return VerifyRuntimeMarketHooks(root.FullName);
        var method=CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName,"mod/Scripts/PeopleOfTheIliacBayMod.Dialogue.cs")))
            .GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(value=>value.Identifier.ValueText=="UpdateEmbeddedDialogue");
        // Execute the production entry prefix before the ordinary Talk window is read.
        string prefix=string.Concat(method.Body.Statements.TakeWhile(value=>!(value is LocalDeclarationStatementSyntax)).Select(value=>value.ToFullString()));
        var redirect=CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root.FullName,"mod/Scripts/PeopleOfTheIliacBayMod.NativeVendorTrade.cs")))
            .GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(value=>value.Identifier.ValueText=="TryRedirectNativeMarketTalk");
        string guard=string.Concat(redirect.Body.Statements.TakeWhile(value=>!(value is LocalDeclarationStatementSyntax declaration &&
            declaration.Declaration.Type is IdentifierNameSyntax type && type.Identifier.ValueText=="NativeMarketPost")).Select(value=>value.ToFullString()));
        string fixture=@"using System;
public class StaticNPC {public static implicit operator bool(StaticNPC value)=>value!=null;}
public class DaggerfallTalkWindow {}
public class TalkManager {public enum NPCType {Static} public NPCType CurrentNPCType;public StaticNPC StaticNPC=new StaticNPC();}
public class GameManager {public static GameManager Instance=new GameManager();public TalkManager TalkManager=new TalkManager();}
public class WindowManager {public object TopWindow;public bool Open=true;public bool ContainsWindow(object window)=>Open;}
public class DaggerfallUI {public static WindowManager UIManager=new WindowManager();}
public class Options {public bool Enabled=true;}
public sealed class Fixture {
  bool redirect; int redirects,normal,prewarm;
  bool redirectingNativeMarket,saveFault;object store=new object();Options options=new Options();
  StaticNPC nativeMarketTalkOnce;DaggerfallTalkWindow nativeMarketTalkWindow;
  bool RedirectGuard(){ GUARD return true; }
  bool TryRedirectNativeMarketTalk(){redirects++;return redirect;}
  void QueueDialoguePrewarm(){prewarm++;}
  void Tick(){ PREFIX normal++; }
  public static int Run(){
    var market=new Fixture{redirect=true};market.Tick();
    bool handled=market.redirects==1 && market.normal==0 && market.prewarm==0;
    Console.WriteLine((handled?""PASS "":""FAIL "")+""native market entry reaches guarded redirect before ordinary dialogue"");
    var ordinary=new Fixture();ordinary.Tick();
    bool continued=ordinary.redirects==1 && ordinary.normal==1 && ordinary.prewarm==1;
    Console.WriteLine((continued?""PASS "":""FAIL "")+""unhandled entry preserves ordinary dialogue prewarm and processing"");
    var guardFixture=new Fixture();var talk=new DaggerfallTalkWindow();DaggerfallUI.UIManager.TopWindow=talk;
    guardFixture.nativeMarketTalkOnce=GameManager.Instance.TalkManager.StaticNPC;
    bool first=!guardFixture.RedirectGuard(),second=!guardFixture.RedirectGuard();
    Console.WriteLine((first && second?""PASS "":""FAIL "")+""market Talk bypass lasts through the actual open dialogue window"");
    DaggerfallUI.UIManager.Open=false;DaggerfallUI.UIManager.TopWindow=null;guardFixture.RedirectGuard();
    DaggerfallUI.UIManager.Open=true;DaggerfallUI.UIManager.TopWindow=talk;
    bool reopened=guardFixture.RedirectGuard();
    Console.WriteLine((reopened?""PASS "":""FAIL "")+""closing Talk restores market activation for the reused native window"");
    return handled && continued && first && second && reopened?0:1;
  }
}".Replace("PREFIX",prefix).Replace("GUARD",guard);
        var references=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(p=>MetadataReference.CreateFromFile(p));
        var compilation=CSharpCompilation.Create("MarketUiEntryFixture",new[]{CSharpSyntaxTree.ParseText(fixture)},references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream=new MemoryStream();var result=compilation.Emit(stream);
        if(!result.Success)throw new InvalidOperationException(string.Join(Environment.NewLine,result.Diagnostics));
        return (int)Assembly.Load(stream.ToArray()).GetType("Fixture").GetMethod("Run").Invoke(null,null);
    }

    static int VerifyMarketArrival(string root)
    {
        var methods=CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,"mod/Scripts/StreetPopulation.MarketCommuters.cs")))
            .GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().ToArray();
        var tick=methods.Single(value=>value.Identifier.ValueText=="TickMarketCommuter");
        string goal=tick.DescendantNodes().OfType<VariableDeclaratorSyntax>().Single(value=>value.Identifier.ValueText=="homeDoor").Initializer.Value.ToString();
        string move=methods.Single(value=>value.Identifier.ValueText=="MarketMove").ToFullString();
        string fixture=@"using System;using System.Collections.Generic;
public struct Vector3 {public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);public static float Distance(Vector3 a,Vector3 b){var d=a-b;return (float)Math.Sqrt(d.x*d.x+d.y*d.y+d.z*d.z);}public static Vector3 MoveTowards(Vector3 a,Vector3 b,float step)=>a;}
public class RoutePoint {public double X,Z;public RoutePoint(double x,double z){X=x;Z=z;}}
public class TownGeometry {public class Entrance {public Vector3 LocalDoor=new Vector3(0,1,0);}public Vector3 SceneDoor(Entrance door)=>door.LocalDoor;public Vector3 ScenePoint(RoutePoint p)=>new Vector3((float)p.X,0,(float)p.Z);}
public class Transform {public Vector3 position;}public class NativeMarketCommuter {public Transform transform=new Transform();public Vector3 Offset=new Vector3(0,1,0),RouteGoal;public List<Vector3> Route;public int Next;}
public static class Time {public static float deltaTime=0;}public static class InteriorGeometry {public static void OpenOrdinaryDoors(Vector3 a,Vector3 b){}}
public class Fixture {bool interior=false;TownGeometry geometry=new TownGeometry();
Vector3 HomeGoal(){var door=new TownGeometry.Entrance();return GOAL;}
// MOVE
public static int Run(){var f=new Fixture();int failures=0;foreach(float gap in new[]{0.3752914f,0.399f,0.8f}){var goal=f.HomeGoal();var body=new NativeMarketCommuter();body.transform.position=new Vector3(gap,0,0)+body.Offset;body.RouteGoal=goal;body.Route=new List<Vector3>{new Vector3(gap,0,0),new Vector3(0,0,0)};bool result=f.MarketMove(body,goal,body.Route,1.3f);bool expected=gap<=0.4f;bool pass=result==expected;Console.WriteLine((pass?""PASS "":""FAIL "")+""market real ground gap=""+gap+"" goalY=""+goal.y+"" arrived=""+result);if(!pass)failures++;}return failures;}}
".Replace("GOAL",goal).Replace("// MOVE",move);
        var refs=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(path=>MetadataReference.CreateFromFile(path));
        var compilation=CSharpCompilation.Create("MarketArrivalFixture",new[]{CSharpSyntaxTree.ParseText(fixture)},refs,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream=new MemoryStream();var result=compilation.Emit(stream);
        if(!result.Success)throw new InvalidOperationException(string.Join(Environment.NewLine,result.Diagnostics));
        return (int)Assembly.Load(stream.ToArray()).GetType("Fixture").GetMethod("Run").Invoke(null,null)==0?0:1;
    }

    static int VerifyMarketClock(string root)
    {
        var commuter=CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,"mod/Scripts/StreetPopulation.MarketCommuters.cs")))
            .GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(value=>value.Identifier.ValueText=="TickMarketCommuter");
        string seconds=commuter.DescendantNodes().OfType<VariableDeclaratorSyntax>().Single(value=>value.Identifier.ValueText=="second").Initializer.Value.ToString();
        var visits=CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,"mod/Scripts/StreetPopulation.MarketVisits.cs")))
            .GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(value=>value.Identifier.ValueText=="PlaceMarketVisit");
        var end=visits.DescendantNodes().OfType<AssignmentExpressionSyntax>().Single(value=>value.Left.ToString()=="EndsAt");
        string expiry=((ConditionalExpressionSyntax)end.Right).WhenFalse.ToString();
        string source="using System; public static class ActualMarketClock { public static double Second(double now) {int day=(int)Math.Floor(now/86400);return "+seconds+
            ";} public static double End(double now,int sleepHour) {int day=(int)Math.Floor(now/86400);return "+expiry+";} }";
        var references=((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")).Split(Path.PathSeparator).Select(value=>MetadataReference.CreateFromFile(value));
        var compilation=CSharpCompilation.Create("ActualMarketClockFixture",new[]{CSharpSyntaxTree.ParseText(source)},references,new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream=new MemoryStream();var result=compilation.Emit(stream);
        if(!result.Success)throw new InvalidOperationException(string.Join(Environment.NewLine,result.Diagnostics));
        var clock=Assembly.Load(stream.ToArray()).GetType("ActualMarketClock");
        double actualNow=145804d*86400d+64620d;
        double actualSecond=(double)clock.GetMethod("Second").Invoke(null,new object[]{actualNow});
        double actualEnd=(double)clock.GetMethod("End").Invoke(null,new object[]{145805d*86400d+43200d,21});
        bool secondPass=actualSecond==64620d;
        bool expiryPass=actualEnd==145805d*86400d+45000d;
        Console.WriteLine((secondPass?"PASS":"FAIL")+" real native epoch preserves second64620 before closing64680; actual="+actualSecond);
        Console.WriteLine((expiryPass?"PASS":"FAIL")+" real native epoch preserves future1800-second market visit; actual="+actualEnd);
        Console.WriteLine((secondPass?1:0)+(expiryPass?1:0)+"/2 actual market clock cases passed");return secondPass && expiryPass?0:1;
    }

    static int VerifyRuntimeMarketHooks(string root)
    {
        MethodDeclarationSyntax Read(string file,string name)=>CSharpSyntaxTree.ParseText(File.ReadAllText(Path.Combine(root,"mod/Scripts",file)))
            .GetRoot().DescendantNodes().OfType<MethodDeclarationSyntax>().Single(value=>value.Identifier.ValueText==name && value.Body!=null);
        InvocationExpressionSyntax[] Calls(MethodDeclarationSyntax method,string name)=>method.DescendantNodes().OfType<InvocationExpressionSyntax>()
            .Where(value=>(value.Expression is IdentifierNameSyntax id?id.Identifier.ValueText:
                value.Expression is MemberAccessExpressionSyntax member?member.Name.Identifier.ValueText:"")==name).ToArray();
        int At(MethodDeclarationSyntax method,string name)=>Calls(method,name).Select(value=>value.SpanStart).DefaultIfEmpty(-1).First();
        int passed=0,total=0;
        void Check(string name,bool condition){total++;if(condition)passed++;Console.WriteLine((condition?"PASS ":"FAIL ")+name);}
        var update=Read("StreetPopulation.cs","UpdatePopulation");
        int tick=At(update,"TickMarketCommuters");
        Check("normal update owns market commute before interior early return",tick>At(update,"UpdateCorpses") && tick<At(update,"UpdateInterior"));
        int place=At(update,"PlaceMarketVisit");
        Check("market visit placement follows action and alarm movement before ordinary waiting",place>At(update,"ApplyNpcActionOverlay") &&
            place>At(update,"Move") && place<At(update,"PlaceClosedShopWait"));
        Check("market visit state participates in normal outdoor visibility",At(update,"MarketVisitState")>=0 && At(update,"MarketVisitState")<At(update,"ApplyNpcActionOverlay"));
        var clear=Read("StreetPopulation.cs","Clear");
        Check("town teardown releases existing market movement and reservations",At(clear,"ReleaseMarketCommuters")>=0 && At(clear,"ResetMarketVisits")>=0);
        Check("managed state reads actual market commuter location",At(Read("StreetPopulation.cs","GetState"),"TryMarketCommuterState")>=0);
        Check("commute acceptance observes production without manual ticks",Calls(Read("StreetPopulation.MarketCommuteProbe.cs","VerifyMarketCommute"),"TickMarketCommuters").Length==0);
        Check("visit acceptance observes production without manual placement",Calls(Read("StreetPopulation.MarketVisitProbe.cs","RunMarketVisitNativeProbe"),"PlaceMarketVisit").Length==0);
        var commuter=Read("StreetPopulation.MarketCommuters.cs","TickMarketCommuter");
        Check("market routine yields to existing accepted action and task",At(commuter,"ActiveNpcAction")>=0 && At(commuter,"ActiveNpcTask")>=0);
        Check("market flight releases shared social pause before escape movement",At(Read("StreetPopulation.MarketCommuters.cs","MarketFlee"),"ReleaseFlightPause")>=0 &&
            commuter.DescendantNodes().OfType<IfStatementSyntax>().Any(value=>value.Statement.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>().Any(call=>call.Expression.ToString()=="MarketFlee") &&
                value.Condition.ToString().Contains("CivilianReaction.Flee")));
        var marketSetup=Read("StreetPopulation.MarketCommuters.cs","TickMarketCommuters");
        Check("cached exterior bodies are never rebound as visible stationary people",marketSetup.DescendantNodes().OfType<IfStatementSyntax>().Any(value=>
            value.Condition.ToString()=="post.Seller.gameObject.activeInHierarchy" && value.Statement.DescendantNodesAndSelf().OfType<InvocationExpressionSyntax>()
                .Any(call=>call.Expression is MemberAccessExpressionSyntax access && access.Name.Identifier.ValueText=="InitializeStationary")));
        Console.WriteLine(passed+"/"+total+" market integration contracts passed");return passed==total?0:1;
    }
}
