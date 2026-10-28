using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
public class NativeObject {public static implicit operator bool(NativeObject value)=>value!=null;public GameObject gameObject=new GameObject();}
public class GameObject {public bool activeInHierarchy=true,activeSelf=true;public void SetActive(bool active){activeSelf=activeInHierarchy=active;}}
public class Transform:NativeObject {public Transform parent;public int position;public bool IsChildOf(Transform root)=>this==root;}
public class Directory:NativeObject {public int MapID=1;public Transform transform=new Transform();}
public class DaggerfallDungeon:NativeObject {public Transform transform=new Transform();}
public class Room:NativeObject {}
public class EnterExit:NativeObject {public bool IsPlayerInsideBuilding,IsPlayerInside=true,IsPlayerInsideDungeon=true;public Room Interior;public DaggerfallDungeon Dungeon=new DaggerfallDungeon();}
public class Stream:NativeObject {public bool IsInit,IsRepositioningPlayer,IsReady=true;public Directory Current;public Directory GetCurrentBuildingDirectory()=>Current;}
public class MapData {public int MapId=1;}
public class Location {public MapData MapTableData=new MapData();}
public class GPS {public bool HasCurrentLocation=true;public Location CurrentLocation=new Location();}
public class Saves {public bool LoadInProgress;}
public class GameManager:NativeObject
{
    public static GameManager Instance;public static bool IsGamePaused;public bool IsReady=true,Playing=true;
    public Stream StreamingWorld=new Stream();public EnterExit PlayerEnterExit=new EnterExit();public GPS PlayerGPS=new GPS();public Saves SaveLoadManager=new Saves();
    public bool IsPlayingGame()=>Playing;
}
public class Clock {public double Second=0.25;public ulong ToSeconds()=>100;}
public class TimeWorld {public Clock Now=new Clock();}
public class DaggerfallUnity {public static DaggerfallUnity Instance=new DaggerfallUnity();public TimeWorld WorldTime=new TimeWorld();}
public enum NpcCustodyPhase {Escorting,Confined,Returning,Released,Cancelled}
public class Point {public string WorldSpaceId="dungeon-1";}
public class Row {public string GuardId="actual-guard",CellDoorId="42";public NpcCustodyPhase Phase=NpcCustodyPhase.Confined;public Point LastPosition=new Point(),Cell=new Point();}
public class Store {public Row Row=new Row();public bool HasInventory=true;public Row ReadNpcCustody(string id)=>Row;public object ReadNpcInventory(string id)=>HasInventory?new object():null;}
public class Person {public string Id="actual-person";}
public class Plan {public Person Resident=new Person();}
public class NativeNpcInventoryOwner:NativeObject {public bool Bound=true;public bool IsBoundTo(Store store)=>Bound;}
public class ResidentActor:NativeObject
{public Transform transform;public NativeNpcInventoryOwner Owner=new NativeNpcInventoryOwner();public T GetComponent<T>() where T:class=>Owner as T;}
public class Door:NativeObject {public ulong LoadID=42;}
public class Cell
{public Transform Root;public Door Door=new Door();public bool Current=true,Match=true;public string Space="dungeon-1";public bool Matches(Row row)=>Match;}
public static class NativeNpcCustody {public static Cell Cell;}
public class Session:NativeObject
{public Store store;public string person="actual-person";public Cell cell;public ResidentActor actor;public GuardActor Guard;
    // ACTUAL_SESSION_OWNERSHIP
}
public class GuardActor:NativeObject {public string GuardId="actual-guard";public Transform transform;public Session CustodyEscort;public int Captures;public void Capture(double now){Captures++;}public void EndCombat(){}}
public class GuardPopulation
{
    public Dictionary<string,GuardActor> actors=new Dictionary<string,GuardActor>();double Now()=>100.25;
    public void Run()=>HideViews();
    // ACTUAL_HIDE
}
public enum GuardDuty {Engaged,Posted}
public class InteriorGuard {public bool Returning;}
public class GuardRecord
{public string Id="actual-guard";public bool IsAlive=true;public GuardDuty Duty=GuardDuty.Engaged;public InteriorGuard Interior;public Point EngagementPosition=new Point(),LastSeenSuspect=new Point();}
public class Roster
{public List<GuardRecord> Guards=new List<GuardRecord>{new GuardRecord()};public Point Observed,Seen;public double Health;
    public void ObserveEngagement(string id,Point point,double health,double now,bool sees,Point suspect){Observed=point;Seen=suspect;Health=health;}
}
public class NativeEntity {public int CurrentHealth=75,MaxHealth=100;}
public static class CivilianResponses {public static bool Sees(int first,int last,int distance)=>true;}
public class CaptureOwner
{
    public string GuardId="actual-guard";public bool IsInterior,IsDungeonCustodyBody=true;public Roster roster=new Roster();
    public NativeEntity entity=new NativeEntity();public Transform transform=new Transform();public double displayedAt;public int ForeignCoordinateCaptures;
    public bool Run(ResidentActor prisoner,double now)
    {// ACTUAL_CAPTURE_PREFIX
        ForeignCoordinateCaptures++;return true;
    }
}
public class Population
{
    public Directory directory=new Directory();public bool Ready=true;public Store worldStore=new Store();
    public List<Plan> plans=new List<Plan>{new Plan()};public Dictionary<string,ResidentActor> actors=new Dictionary<string,ResidentActor>();
    public int Ticks,NormalTicks;public double ObservedNow;public ResidentActor ObservedActor;
    void Clear(){}
    bool ApplyNpcCustodyOverlay(Plan plan,ref ResidentActor actor,double now)
    {Ticks++;ObservedNow=now;ObservedActor=actor;return true;}
    public void Run(){// ACTUAL_OWNER_PREFIX
        NormalTicks++;
    }
    // ACTUAL_DUNGEON_CUSTODY
}
public static class Fixture
{
    static int checks;
    static Population Prepare()
    {
        var population=new Population();GameManager.Instance=new GameManager();GameManager.IsGamePaused=false;
        GameManager.Instance.StreamingWorld.Current=population.directory;population.directory.gameObject.activeInHierarchy=false;
        NativeNpcCustody.Cell=new Cell {Root=GameManager.Instance.PlayerEnterExit.Dungeon.transform};
        population.actors.Add("actual-person",new ResidentActor {transform=NativeNpcCustody.Cell.Root});return population;
    }
    static void Need(bool value,string text){if(!value)throw new InvalidOperationException(text);checks++;Console.WriteLine("PASS "+text);}
    public static int Run()
    {
        var population=Prepare();var body=population.actors["actual-person"];population.Run();
        Need(population.Ticks==1 && population.NormalTicks==0 && population.ObservedActor==body && population.ObservedNow==100.25,
            "actual registered DungeonRoot keeps the same custody body ticking without exterior routines");
        population=Prepare();NativeNpcCustody.Cell=null;population.Run();Need(population.Ticks==0,"no registered cell creates no dungeon custody execution");
        population=Prepare();NativeNpcCustody.Cell.Root=new Transform();population.Run();Need(population.Ticks==0,"another dungeon root cannot claim custody");
        population=Prepare();NativeNpcCustody.Cell.Current=false;population.Run();Need(population.Ticks==0,"navigation-not-ready cell cannot advance custody");
        population=Prepare();population.worldStore.Row.LastPosition.WorldSpaceId="settlement-1";population.Run();Need(population.Ticks==0,"exterior position cannot fabricate portal admission");
        population=Prepare();population.actors["actual-person"].transform=new Transform();population.Run();Need(population.Ticks==0,"foreign body is not reparented or duplicated into dungeon");
        population=Prepare();population.actors["actual-person"].Owner.Bound=false;population.Run();Need(population.Ticks==0,"another save branch cannot own dungeon body");
        population=Prepare();population.worldStore.HasInventory=false;population.Run();Need(population.Ticks==0,"missing existing inventory cannot initialize prisoner belongings");
        population=Prepare();GameManager.IsGamePaused=true;population.Run();Need(population.Ticks==0,"native pause cannot advance custody");
        population=Prepare();GameManager.Instance.SaveLoadManager.LoadInProgress=true;population.Run();Need(population.Ticks==0,"native reload cannot advance custody");
        population=Prepare();population.worldStore.Row.Phase=NpcCustodyPhase.Escorting;
        var guards=new GuardPopulation();var escort=new GuardActor {transform=NativeNpcCustody.Cell.Root};var ordinary=new GuardActor();
        escort.CustodyEscort=new Session {cell=NativeNpcCustody.Cell,actor=population.actors["actual-person"],store=population.worldStore,Guard=escort};
        guards.actors.Add("actual-escort",escort);guards.actors.Add("ordinary-street",ordinary);guards.Run();
        Need(escort.gameObject.activeSelf && escort.Captures==0 && !ordinary.gameObject.activeSelf,
            "street hiding preserves the actual Dungeon custody escort while hiding ordinary street guards");
        NativeNpcCustody.Cell.Current=false;
        Need(escort.CustodyEscort.OwnsLoadedDungeon,"temporary native navigation readiness cannot strip a real loaded escort's visibility owner");
        escort.CustodyEscort.Guard.GuardId="another-guard";guards.Run();Need(!escort.gameObject.activeSelf,"stale custody session cannot retain guard visibility ownership");
        var capture=new CaptureOwner();var anchor=capture.roster.Guards[0].EngagementPosition;var suspect=capture.roster.Guards[0].LastSeenSuspect;
        capture.transform.parent=body.transform;Need(capture.Run(body,100.25) && capture.roster.Observed==anchor && capture.roster.Seen==suspect &&
            capture.roster.Health==0.75 && capture.ForeignCoordinateCaptures==0,"actual dungeon guard capture renews health and sight without replacing exterior return coordinates");
        capture=new CaptureOwner();capture.roster.Guards[0].EngagementPosition=null;
        Need(!capture.Run(body,100.25) && capture.roster.Observed==null,"missing actual guard return anchor cannot be manufactured from dungeon coordinates");
        Console.WriteLine(checks+" checks passed");return 0;
    }
}
