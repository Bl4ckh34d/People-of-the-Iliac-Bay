using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
public struct Vector3
{
    public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}public static Vector3 up=>new Vector3(0,1,0);
    public float sqrMagnitude=>x*x+y*y+z*z;
    public static Vector3 operator+(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
    public static Vector3 operator-(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
    public static Vector3 operator*(Vector3 a,float n)=>new Vector3(a.x*n,a.y*n,a.z*n);
    public static float Distance(Vector3 a,Vector3 b)=>(float)Math.Sqrt((a-b).sqrMagnitude);
}
public struct Vector2{public float x,y;public Vector2(float a,float b){x=a;y=b;}}
public sealed class Transform{public Vector3 position;public Transform transform=>this;public Vector3 TransformPoint(Vector3 p)=>p;}
public sealed class GameObject{public bool activeInHierarchy=true;}
public sealed class GuardActor
{
    public string GuardId;public bool CombatReady,DefenseTarget;public Transform transform=new Transform();public GameObject gameObject=new GameObject();public GuardRoster Roster;
    public static implicit operator bool(GuardActor a)=>a!=null;
    public void BeginCombat(double now,SavedRoutePoint suspect=null){Roster.Engage(GuardId,now);var row=Roster.Guards.Find(g=>g.Id==GuardId);row.LastSeenSuspect=suspect;CombatReady=true;}
}
public sealed class DaggerfallEntityBehaviour{public Transform transform=new Transform();public static implicit operator bool(DaggerfallEntityBehaviour p)=>p!=null;}
public sealed class PlayerEntity {public enum Crimes{None,Murder}public Crimes CrimeCommitted;public bool IsInvisible;}
public sealed class GameManager{public static GameManager Instance=new GameManager();public static bool IsGamePaused;public PlayerEntity PlayerEntity=new PlayerEntity();public Transform PlayerMotor=new Transform();public DaggerfallEntityBehaviour PlayerEntityBehaviour=new DaggerfallEntityBehaviour();}
public sealed class WorldTime{public double TimeScale=1;}
public sealed class DaggerfallUnity{public static DaggerfallUnity Instance=new DaggerfallUnity();public WorldTime WorldTime=new WorldTime();}
public static class CivilianResponses{public static bool Blocked;public static bool Sees(Vector3 from,Vector3 to,float range)=>!Blocked && Vector3.Distance(from,to)<=range;}
public static class Debug{public static void Log(string text){}}
public sealed class RoutePoint{public double X,Z;public RoutePoint(double x,double z){X=x;Z=z;}}
public sealed class Geometry
{
    public bool BlockRoutes;public Vector3 ScenePoint(RoutePoint p)=>new Vector3((float)p.X,0,(float)p.Z);
    public List<SavedRoutePoint> RouteBetweenPoints(SavedRoutePoint a,SavedRoutePoint b)=>BlockRoutes?new List<SavedRoutePoint>():new List<SavedRoutePoint>{new SavedRoutePoint{X=a.X,Z=a.Z},new SavedRoutePoint{X=b.X,Z=b.Z}};
}
public sealed class NpcData{public int billboardArchiveIndex,billboardRecordIndex,factionID;}
public sealed class StaticNPC {public GameObject gameObject=new GameObject();public Transform transform=new Transform();public NpcData Data=new NpcData();public static implicit operator bool(StaticNPC n)=>n!=null;public T GetComponent<T>() where T:class=>null;}
public sealed class SleepingPose{public bool Active;public static implicit operator bool(SleepingPose p)=>p!=null;}
public static class RDBLayout{public static bool IsNPCFlat(int n)=>true;}
public sealed class Directory{public int MapID=1;public Transform transform=new Transform();public List<StaticNPC> Npcs=new List<StaticNPC>();public T[] GetComponentsInChildren<T>()=>Npcs.ToArray() as T[];}
public sealed class ResidentActor{public Transform transform=new Transform();}
public sealed class StreetPopulation{public static implicit operator bool(StreetPopulation p)=>p!=null;public List<ResidentActor> People=new List<ResidentActor>();public List<ResidentActor> NearbyCorpseInquiryPeople(Vector3 p,float radius=4)=>People;}
public sealed class Fixture
{
    private GuardRoster Roster;private bool interior,OwnsExterior=true;private Dictionary<string,GuardActor> actors=new Dictionary<string,GuardActor>();
    private Geometry geometry=new Geometry();private WorldStateStore worldStore=new WorldStateStore(42);private Directory directory=new Directory();private StreetPopulation streets=new StreetPopulation();
    private T GetComponent<T>() where T:class=>streets as T;
    private static SavedRoutePoint Local(Vector3 p)=>new SavedRoutePoint{X=p.x,Z=p.z};
    private static double Now()=>100;
    private bool RecruitNearbyCorpseSearchGuards(double now,GuardActor caller)=>false;
    private string RegisterHostileThreat(DaggerfallEntityBehaviour target)=>"enemy";
    // PRODUCTION_METHODS
    private static int checks,failed;
    private static void Check(bool value,string label){checks++;if(!value)failed++;Console.WriteLine((value?"PASS ":"FAIL ")+label);}
    private static Fixture Create(int count=9)
    {
        var f=new Fixture();var posts=new List<SavedRoutePoint>{new SavedRoutePoint{X=-15}};
        for(int i=1;i<count;i++)posts.Add(new SavedRoutePoint{X=12+i*.5});
        f.Roster=GuardRoster.Create(1,42,count,posts);
        foreach(var g in f.Roster.Guards)f.actors[g.Id]=new GuardActor{GuardId=g.Id,Roster=f.Roster,transform=new Transform{position=new Vector3((float)g.Post.X,0,(float)g.Post.Z)}};
        var caller=f.Roster.Guards[0];f.Roster.Dispatch("crime",100,new Dictionary<string,List<SavedRoutePoint>>{{caller.Id,new List<SavedRoutePoint>{caller.Post,caller.Post}}},1,2.8,1);
        f.Roster.Engage(caller.Id,100);caller.LastSeenSuspect=new SavedRoutePoint();caller.SearchUntil=500;f.actors[caller.Id].CombatReady=true;
        GameManager.Instance=new GameManager();GameManager.Instance.PlayerEntity.CrimeCommitted=PlayerEntity.Crimes.Murder;CivilianResponses.Blocked=false;
        return f;
    }
    public static int Run()
    {
        var f=Create();int identities=f.Roster.Guards.Count;f.RecruitNearbyPursuitGuards(100);
        Check(f.Roster.Guards.FindAll(g=>g.Duty==GuardDuty.Engaged).Count==9,"all eight stationary guards seeing pursuit join beyond old contact radius");
        Check(f.Roster.Guards.Count==identities,"visual recruitment preserves finite roster identities");
        f=Create();CivilianResponses.Blocked=true;f.RecruitNearbyPursuitGuards(100);Check(f.Roster.Guards.FindAll(g=>g.Duty==GuardDuty.Engaged).Count==1,"occlusion blocks visual recruitment");
        f=Create();GameManager.Instance.PlayerEntity.IsInvisible=true;f.RecruitNearbyPursuitGuards(100);Check(f.Roster.Guards.FindAll(g=>g.Duty==GuardDuty.Engaged).Count==1,"invisible player grants no visual recruitment");
        f=Create(4);var caller=f.actors[f.Roster.Guards[0].Id];
        // Player distance is irrelevant to hearing. These posts are measured from the shouting guard.
        f.Roster.Guards[1].Post.X=184;f.actors[f.Roster.Guards[1].Id].transform.position=new Vector3(184,0,0);
        f.Roster.Guards[2].Post.X=186;f.actors[f.Roster.Guards[2].Id].transform.position=new Vector3(186,0,0);
        f.Roster.Guards[3].Post.X=185;f.actors[f.Roster.Guards[3].Id].transform.position=new Vector3(185,0,0);
        GameManager.Instance.PlayerEntityBehaviour.transform.position=new Vector3(-30,0,0);CivilianResponses.Blocked=true;
        f.HearGuardHalt(caller,GameManager.Instance.PlayerEntityBehaviour);
        Check(f.Roster.Guards[1].Duty==GuardDuty.Responding,"actual Halt recruits out-of-sight guard 199m from shouter");
        Check(f.Roster.Guards[2].Duty==GuardDuty.Posted,"Halt excludes guard 201m from shouter");
        Check(f.Roster.Guards[3].Duty==GuardDuty.Responding,"actual Halt includes guard exactly200m from shouter");
        f=Create();GameManager.Instance.PlayerEntity.CrimeCommitted=PlayerEntity.Crimes.None;
        f.HearGuardHalt(f.actors[f.Roster.Guards[0].Id],GameManager.Instance.PlayerEntityBehaviour);
        Check(f.Roster.Reports.Count==1,"Halt without current player crime never invents pursuit");
        f=Create();var alarm=new CivilianIncident{Id="case-a",SettlementId=1,Corpse=new CorpseState()};
        var loop=f.CorpseSearchPatrol(alarm,new SavedRoutePoint());
        var visited=new HashSet<string>();bool offAxis=false;
        if(loop!=null)foreach(var point in loop){visited.Add(point.X.ToString("F2")+":"+point.Z.ToString("F2"));offAxis|=Math.Abs(point.X)>1 && Math.Abs(point.Z)>1;}
        Check(visited.Count>=4 && offAxis,"open scene search visits varied points instead of one mirrored cardinal segment");
        var other=f.CorpseSearchPatrol(new CivilianIncident{Id="case-b",SettlementId=1,Corpse=new CorpseState()},new SavedRoutePoint());
        Check(loop!=null && other!=null && (Math.Abs(loop[1].X-other[1].X)>0.01 || Math.Abs(loop[1].Z-other[1].Z)>0.01),"different cases receive varied reachable scene routes");
        f.directory.Npcs.Add(new StaticNPC{transform=new Transform{position=new Vector3(10,0,5)}});
        loop=f.CorpseSearchPatrol(alarm,new SavedRoutePoint());
        Check(loop!=null && Math.Sqrt(Math.Pow(loop[1].X-10,2)+Math.Pow(loop[1].Z-5,2))<=2.1,"search physically approaches visible NPC without prior witness classification");
        Check(alarm.People.Count==0 && !alarm.AccusationIssued,"planning NPC contact grants no witness statement or accusation");
        CivilianResponses.Blocked=true;Check(f.CorpseInquiryDestinations(new SavedRoutePoint()).Count==0,"occluded NPC grants no inquiry destination");CivilianResponses.Blocked=false;
        f.geometry.BlockRoutes=true;Check(f.CorpseSearchPatrol(alarm,new SavedRoutePoint())==null,"unreachable scene points never invent a route");
        Console.WriteLine((checks-failed)+"/"+checks+" passed");return failed==0?0:1;
    }
}
