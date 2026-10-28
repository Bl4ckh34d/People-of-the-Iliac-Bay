using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
public struct Vector3
{
    public float x,y,z; public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
    public static Vector3 up=>new Vector3(0,1,0);
    public static Vector3 zero=>new Vector3();public float sqrMagnitude=>x*x+y*y+z*z;
    public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
    public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
    public static float Distance(Vector3 a,Vector3 b)=>(float)Math.Sqrt((a.x-b.x)*(a.x-b.x)+(a.y-b.y)*(a.y-b.y)+(a.z-b.z)*(a.z-b.z));
}
public sealed class Transform{public Vector3 position;public Vector3 InverseTransformPoint(Vector3 p)=>p;}
public sealed class Resident {public string Id="resident";public bool IsAlive=true;}
public sealed class Person{public string NameNPC="Aubara";}
public sealed class ResidentActor
{
    public Resident Resident=new Resident();public Person Person=new Person();public string DisplayName=>Person.NameNPC;
    public Transform transform=new Transform();public Vector3 GroundPosition=>transform.position;
    public static implicit operator bool(ResidentActor a)=>a!=null;
}
public sealed class MobilePersonNPC
{
    public ResidentActor Actor=new ResidentActor();public static implicit operator bool(MobilePersonNPC p)=>p!=null;
    public T GetComponent<T>()where T:class=>Actor as T;
}
public sealed class StaticNPC{public object Data;public string DisplayName="Uthayne";public static implicit operator bool(StaticNPC p)=>p!=null;}
public sealed class WorldTime{public Clock Now=new Clock();public double TimeScale=1;}
public sealed class Clock{public double ToSeconds()=>100;}
public sealed class DaggerfallUnity{public static DaggerfallUnity Instance=new DaggerfallUnity();public WorldTime WorldTime=new WorldTime();}
public static class DaggerfallUI{public static string Last;public static void AddHUDText(string value,float seconds=0){Last=value;}}
public sealed class Store
{
    public bool HoldsPartnerGrudge(Resident r,double now)=>false;
    public NpcConversationBoundaryRecord ReadNpcConversationBoundary(string person,double now)=>new NpcConversationBoundaryRecord {
        ReceiptId="receipt",SpeechKind=NpcExperienceSpeechActKind.DirectThreat,Action=ConversationSpeechBoundaryAction.EndAndReport,
        ReportStatus=NpcConversationBoundaryReportStatus.Started };
}
public sealed class StreetPopulation
{
    public bool Reporting,Alarmed;public bool HasDialogueComplaint(string id)=>Reporting;public bool IsAlarmed(string id)=>Alarmed;
    public static implicit operator bool(StreetPopulation s)=>s!=null;
}
public sealed class Hud
{
    public bool AllowBoundary=true,Terminal;private bool saveFault,conversationDispositionEnabled=true;
    public StreetPopulation streets=new StreetPopulation();private Store store=new Store();
    private bool CanApproachNpcAfterBoundary(string id)=>AllowBoundary;
    private bool HasTerminalConversationHostility(string id)=>Terminal;
    private string StaticConversationPersonId(StaticNPC person)=>"static";
    // HUD_METHODS
    public bool Mobile(MobilePersonNPC p)=>CanStartMobileConversation(p);
    public bool Static(StaticNPC p)=>CanStartStaticConversation(p);
}
public static class CivilianResponses{public static bool Visible=true;public static bool Sees(Vector3 a,Vector3 b,int range)=>Visible;}
public static class Time{public static float time;}
public static class Debug{public static void Log(string value){}}
namespace UnityEngine{public static class Random{public static float Range(float min,float max)=>3;}}
public sealed class GameObject{public void SetActive(bool value){}}
public sealed class GuardActor
{
    public bool Holding,CombatReady,DefenseTarget,IsSleeping;public GameObject gameObject=new GameObject();
    public static implicit operator bool(GuardActor g)=>g!=null;
    public void BeginReportConversation(ResidentActor witness,float seconds){Holding=true;}
    public bool HoldReportConversation()=>Holding;
    public bool HoldsInquiry(ResidentActor witness)=>Holding;
    public void EndReportConversation(){Holding=false;}
}
public sealed class GuardFixture
{
    public int Dispatches,Begins;public bool Available=true;public GuardActor Listener=new GuardActor();
    private double Now()=>100;
    private bool TryGetComplaintGuardPosition(string id,out Vector3 point){point=new Vector3();return Available;}
    private bool TryReportConversationBoundaryAt(string receipt,string person,Vector3? point,int? key,string receiver=null){Dispatches++;return true;}
    private GuardActor BeginWitnessReportConversation(string id,ResidentActor witness,double at,string account,string answer){Begins++;return Listener;}
    private Store worldStore=new Store();private FakeRoster Roster=new FakeRoster();
    private bool OwnsTown()=>true;public Geometry geometry=new Geometry();private Directory directory=new Directory();
    private GuardActor View(FakeGuard guard,double at)=>Listener;
    private sealed class BoundaryReportConversation
    {public string ReceiptId,GuardId;public ResidentActor Witness;public GuardActor Listener;public float CompletesAt;}
    private readonly Dictionary<string,BoundaryReportConversation> boundaryReportConversations=new Dictionary<string,BoundaryReportConversation>();
    // GUARD_METHODS
}
public sealed class FakeGuard{public string Id="guard";public bool IsAlive=true;public GuardDuty Duty=GuardDuty.Posted;public object Interior,Refuge;}
public sealed class FakeRoster
{public List<FakeGuard> Guards=new List<FakeGuard>{new FakeGuard()};public void Advance(double at){}public SavedRoutePoint Position(string id,double at)=>new SavedRoutePoint{X=1};}
public sealed class Directory{public Transform transform=new Transform();}
public sealed class RoutePoint{public double X,Z;public RoutePoint(double x,double z){X=x;Z=z;}}
public sealed class Geometry
{
    public bool Reachable=true;public Vector3 ScenePoint(RoutePoint point)=>new Vector3((float)point.X,0,(float)point.Z);
    public List<SavedRoutePoint> RouteBetweenPoints(SavedRoutePoint a,SavedRoutePoint b)=>Reachable?new List<SavedRoutePoint>{a,b}:null;
}
public static class Fixture
{
    private static int count,failed;
    private static void Check(bool ok,string label){count++;if(!ok)failed++;Console.WriteLine((ok?"PASS ":"FAIL ")+label);}
    public static int Run()
    {
        var hud=new Hud{AllowBoundary=false};var npc=new MobilePersonNPC();
        Check(!hud.Mobile(npc)&&DaggerfallUI.Last.StartsWith("Aubara ",StringComparison.Ordinal),"blocked resident HUD identifies real NPC");
        Check(!DaggerfallUI.Last.Contains("guard",StringComparison.OrdinalIgnoreCase),"repeated insult refusal without active complaint does not invent guard fetching");
        hud.streets.Reporting=true;hud.Mobile(npc);
        Check(DaggerfallUI.Last.StartsWith("Aubara ",StringComparison.Ordinal)&&DaggerfallUI.Last.Contains("guard",StringComparison.OrdinalIgnoreCase),"real active complaint explains named guard fetching");
        hud.AllowBoundary=true;hud.streets.Reporting=false;hud.streets.Alarmed=true;hud.Mobile(npc);
        Check(DaggerfallUI.Last.StartsWith("Aubara ",StringComparison.Ordinal),"murder witness distance refusal identifies real NPC");
        hud.AllowBoundary=false;hud.Static(new StaticNPC());
        Check(DaggerfallUI.Last.StartsWith("Uthayne ",StringComparison.Ordinal)&&!DaggerfallUI.Last.Contains("guard",StringComparison.OrdinalIgnoreCase),"static refusal cannot claim unstarted physical complaint");
        var guards=new GuardFixture();var witness=new ResidentActor();Time.time=0;
        Check(!guards.TryReportConversationBoundaryAtGuard("receipt","resident",new Vector3(),"guard",witness)&&guards.Dispatches==0,
            "physical guard contact starts conversation before any crime or report dispatch");
        Time.time=2;
        Check(!guards.TryReportConversationBoundaryAtGuard("receipt","resident",new Vector3(),"guard",witness)&&guards.Dispatches==0,
            "report remains undelivered during physical conversation");
        Time.time=3;
        Check(guards.TryReportConversationBoundaryAtGuard("receipt","resident",new Vector3(),"guard",witness)&&guards.Dispatches==1,
            "same witnessed threat dispatches after completed conversation");
        guards=new GuardFixture();Time.time=0;guards.TryReportConversationBoundaryAtGuard("receipt","resident",new Vector3(),"guard",witness);
        guards.Available=false;Time.time=1;guards.TryReportConversationBoundaryAtGuard("receipt","resident",new Vector3(),"guard",witness);
        guards.Available=true;Time.time=4;
        Check(!guards.TryReportConversationBoundaryAtGuard("receipt","resident",new Vector3(),"guard",witness)&&guards.Dispatches==0,
            "lost guard contact cancels elapsed conversation instead of fabricating delivery");
        guards=new GuardFixture();guards.geometry.Reachable=false;CivilianResponses.Visible=false;
        Check(!guards.TryFindNearestComplaintGuard(new Vector3(),out _,out _,out _),
            "nearby guard behind obstruction without route is not fabricated as reachable contact");
        Console.WriteLine((count-failed)+"/"+count+" passed");return failed==0?0:1;
    }
}
