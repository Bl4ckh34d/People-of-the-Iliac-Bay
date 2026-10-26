using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

public struct Vector3
{
    public float x,y,z;
    public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
    public static Vector3 zero=>new Vector3();public static Vector3 up=>new Vector3(0,1,0);
    public float magnitude=>(float)Math.Sqrt(x*x+y*y+z*z);
    public Vector3 normalized=>magnitude==0?zero:this*(1/magnitude);
    public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
    public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
    public static Vector3 operator *(Vector3 a,float b)=>new Vector3(a.x*b,a.y*b,a.z*b);
    public static float Distance(Vector3 a,Vector3 b)=>(a-b).magnitude;
    public static float Angle(Vector3 a,Vector3 b)=>a.magnitude*b.magnitude==0?0:(float)(Math.Acos(Math.Clamp((a.x*b.x+a.y*b.y+a.z*b.z)/(a.magnitude*b.magnitude),-1,1))*180/Math.PI);
}
public static class Mathf {public static float Abs(float v)=>Math.Abs(v);public static float Min(float a,float b)=>Math.Min(a,b);}
public class Transform {public Vector3 position;public Vector3 forward=new Vector3(0,0,1);public int rotation;public Vector3 TransformPoint(Vector3 point)=>position+point;}
public class GameObject {public bool activeInHierarchy=true;}
public class NativeObject
{
    public Transform transform=new Transform();public GameObject gameObject=new GameObject();
    private Dictionary<Type,object> components=new Dictionary<Type,object>();
    public void Set<T>(T value)=>components[typeof(T)]=value;
    public T GetComponent<T>() where T:class=>components.TryGetValue(typeof(T),out var value)?(T)value:null;
    public static implicit operator bool(NativeObject value)=>value!=null;
    public static bool operator !(NativeObject value)=>value==null;
}
public enum DoorTypes {Building,Other}
public struct StaticDoor {public DoorTypes doorType;public int buildingKey,blockIndex,recordIndex,doorIndex;public Vector3 centre,size,ownerPosition;public int ownerRotation;}
public class DaggerfallStaticDoors:NativeObject
{
    public StaticDoor[] Doors;
    public Vector3 GetDoorNormal(int i)=>new Vector3(0,0,1);
    public Vector3 GetDoorPosition(int i)=>transform.position+Doors[i].centre;
    public static Vector3 GetDoorNormal(StaticDoor door)=>new Vector3(0,0,1);
    public static Vector3 GetDoorPosition(StaticDoor door)=>door.ownerPosition+door.centre;
}
public class BuildingDirectory:NativeObject
{
    public int MapID=1;public DaggerfallStaticDoors Owner;
    public T[] GetComponentsInChildren<T>(bool includeInactive)=>new[]{(T)(object)Owner};
}
public class Resident {public string Id="resident";public bool IsAlive=true,IsQuestProtected;}
public class ResidentActor:NativeObject
{
    public Resident Resident=new Resident();public bool Standing;
    public void Place(Vector3 ground,Vector3 facing,bool standing){transform.position=ground+Vector3.up;transform.forward=facing;Standing=standing;}
}
public enum MobileTeams {Other,PlayerAlly}
public class Entity {public int CurrentHealth=10;public bool IsInvisible;public MobileTeams Team;}
public class DaggerfallEntityBehaviour:NativeObject {public Entity Entity=new Entity();}
public class EnemySenses:NativeObject {public float FieldOfView=180,SightRadius=30;}
public class EnemyMotor:NativeObject {public bool IsHostile;}
public class GuardActor:NativeObject
{
    public string GuardId;public bool IsInterior;public bool Ended,CombatReady,Searched;public DaggerfallEntityBehaviour DefenseTarget;
    public void Capture(double now){}public void EndCombat(){Ended=true;CombatReady=false;DefenseTarget=null;}
    public void Place(double now){}public void SearchInterior(Room room,double now){Searched=true;}
    public void BeginDefenseCombat(DaggerfallEntityBehaviour target,double now){CombatReady=true;DefenseTarget=target;}
}
public class DefenseScene {public object Root=typeof(DefenseScene);public static DefenseScene For(NativeObject obj)=>new DefenseScene();}
public static class CivilianResponses {public static bool Visible=true;public static bool Sees(Vector3 a,Vector3 b,float range)=>Visible && Vector3.Distance(a,b)<=range;}
public static class GameManager {public static bool IsGamePaused;}
public class Clock {public double TimeScale=1;}
public class DaggerfallUnity {public static DaggerfallUnity Instance=new DaggerfallUnity();public Clock WorldTime=new Clock();}
public static class Debug {public static void Log(string s){}public static void LogWarning(string s){}}
public class TownGeometry
{
    public bool Reachable=true;
    public List<SavedRoutePoint> RouteBetweenPoints(SavedRoutePoint a,SavedRoutePoint b)=>Reachable?new List<SavedRoutePoint>{a,b}:new List<SavedRoutePoint>();
}
public class Room:NativeObject
{
    public StaticDoor EntryDoor;public DaggerfallEntityBehaviour[] Targets=new DaggerfallEntityBehaviour[0];
    public T[] GetComponentsInChildren<T>()=>Array.ConvertAll(Targets,body=>(T)(object)body);
}
public class Settlement {public int SettlementId=1;}
public class StreetPopulation
{
    private WorldStateStore worldStore;private Room interior;private Settlement population=new Settlement();
    public static bool SeesSceneEvent(ResidentActor resident,Vector3 point,float range)=>
        Vector3.Angle(resident.transform.forward,point-resident.transform.position)<90 && CivilianResponses.Sees(resident.transform.position,point,range);
    // DOOR_GROUND
    // SHELTER_HOLD
    public static void TestShelterHold()
    {
        var f=new StreetPopulation{worldStore=new WorldStateStore(31),interior=new Room{EntryDoor=new StaticDoor{buildingKey=7}}};
        var person=f.worldStore.GetOrCreate(1,new[]{7},1).Households[0].Residents[0];
        f.worldStore.RecordDefenseShelter(person.Id,1,"actual",new AlarmPerson{ResidentId=person.Id,BuildingKey=7,
            HasPosition=true,Reaction=CivilianReaction.Flee,ReachedRefuge=true,X=100,Y=0,Z=100},10);
        f.worldStore.ObserveDefenseShelterLanding(person.Id,1,7,"actual",3,4,5,11);
        var restored=new WorldStateStore(0);restored.Restore(f.worldStore.Snapshot());f.worldStore=restored;
        var actor=new ResidentActor{Resident=new Resident{Id=person.Id}};
        var reaction=f.worldStore.ResidentDefenseShelter(person.Id,1).Reaction;
        if(!f.HoldLawfulGuardShelter(actor,reaction,false) || !actor.Standing ||
            Vector3.Distance(actor.transform.position,new Vector3(3,5,5))>0.001)
            throw new Exception("Restored refuge without a visible guard must hold its own recorded ground, not flee the player.");
        if(f.HoldLawfulGuardShelter(actor,reaction,true))throw new Exception("Shelter hold suppressed an actual known danger.");
        f.interior.EntryDoor=new StaticDoor{buildingKey=8};
        if(f.HoldLawfulGuardShelter(actor,reaction,false))throw new Exception("Shelter hold projected position into the wrong room.");
    }
}
public class GuardPopulation
{
    private BuildingDirectory directory=new BuildingDirectory();private TownGeometry geometry=new TownGeometry();
    private Dictionary<string,GuardActor> actors=new Dictionary<string,GuardActor>();
    private GuardRoster Roster;private bool OwnsExterior=true,EnemyDefense=true;
    private Room interior;
    private bool OwnsTown()=>true;
    private static SavedRoutePoint Local(Vector3 point)=>new SavedRoutePoint{X=point.x,Z=point.z};
    private string ThreatKey(DaggerfallEntityBehaviour target)=>"resident";
    private static bool IsReacquirableMurderer(DaggerfallEntityBehaviour target)=>target && target.gameObject.activeInHierarchy && target.Entity!=null && target.Entity.CurrentHealth>0;
    private void DetachWitnessedDoorForPlayerAssault(GuardRecord guard,GuardActor actor,double now)=>throw new Exception("Unexpected player assault.");
    // GUARD_METHODS
    private static GuardPopulation Create(out ResidentActor resident,out StaticDoor door)
    {
        CivilianResponses.Visible=true;
        var f=new GuardPopulation();
        door=new StaticDoor{doorType=DoorTypes.Building,buildingKey=7,blockIndex=2,recordIndex=3,doorIndex=4,
            centre=new Vector3(0,1.5f,0),size=new Vector3(2,3,0)};
        f.directory.Owner=new DaggerfallStaticDoors{Doors=new[]{door}};
        f.directory.Owner.transform.position=new Vector3(20,4,30);
        resident=new ResidentActor();resident.transform.position=new Vector3(20,5,30.6f);
        var target=new DaggerfallEntityBehaviour();target.transform=resident.transform;resident.Set(target);
        var post=new SavedRoutePoint{X=20,Z=25.6f};
        f.Roster=GuardRoster.Create(1,12,2,new[]{post});
        foreach(var guard in f.Roster.Guards)
        {
            f.Roster.DispatchDefense("assault:"+guard.Id,"resident",10,new Dictionary<string,List<SavedRoutePoint>>{
                {guard.Id,new List<SavedRoutePoint>{post,post}}},1,1,1);
            f.Roster.Engage(guard.Id,10);
            var actor=new GuardActor{GuardId=guard.Id,DefenseTarget=target};
            actor.transform.position=new Vector3(20,5,25.6f);actor.Set(new EnemySenses());actor.Set(new EnemyMotor());actor.Set(new DaggerfallEntityBehaviour());f.actors.Add(guard.Id,actor);
        }
        // Second assigned guard faces away. Shared identity is not entry sight.
        f.actors[f.Roster.Guards[1].Id].transform.forward=new Vector3(0,0,-1);
        return f;
    }
    private static void Need(bool value,string message){if(!value)throw new Exception(message);}
    public static int Run()
    {
        ResidentActor resident;StaticDoor door;var f=Create(out resident,out door);
        f.ObserveResidentBuildingEntry(resident,door,11);
        Need(f.Roster.Guards[0].DoorPursuit!=null,"Three-metre native door centre rejected resident at its actual ground.");
        Need(f.Roster.Guards[1].DoorPursuit==null,"Backward guard inherited entry knowledge.");
        Need(Math.Abs(f.Roster.Guards[0].Route[1].Z-30.6)<0.001,"Route did not end at exact door ground.");
        GuardRoster.CopyValidated(f.Roster);
        f=Create(out resident,out door);CivilianResponses.Visible=false;f.ObserveResidentBuildingEntry(resident,door,11);
        Need(f.Roster.Guards[0].DoorPursuit==null,"Occluded guard received entry knowledge.");
        f=Create(out resident,out door);resident.GetComponent<DaggerfallEntityBehaviour>().Entity.IsInvisible=true;
        f.ObserveResidentBuildingEntry(resident,door,11);Need(f.Roster.Guards[0].DoorPursuit==null,"Invisible entrant disclosed entry.");
        f=Create(out resident,out door);door.doorIndex=99;f.ObserveResidentBuildingEntry(resident,door,11);
        Need(f.Roster.Guards[0].DoorPursuit==null,"Unknown native door admitted.");
        f=Create(out resident,out door);resident.transform.position+=new Vector3(0,0,2);f.ObserveResidentBuildingEntry(resident,door,11);
        Need(f.Roster.Guards[0].DoorPursuit==null,"Remote resident treated as crossing.");
        f=Create(out resident,out door);f.geometry.Reachable=false;f.ObserveResidentBuildingEntry(resident,door,11);
        Need(f.Roster.Guards[0].DoorPursuit==null,"Unreachable route admitted.");
        f=Create(out resident,out door);f.Roster.Guards[0].ReturnRate=double.NaN;f.ObserveResidentBuildingEntry(resident,door,11);
        Need(f.Roster.Guards[0].DoorPursuit==null,"Invalid state published pursuit rather than declining optional observation.");
        f=Create(out resident,out door);Vector3 seen;
        Need(!f.TryVisibleResidentPursuer(resident,out seen),"Guard seeing resident does not prove resident sees guard behind them.");
        resident.transform.forward=new Vector3(0,0,-1);
        foreach(var actor in f.actors.Values)actor.transform.forward=new Vector3(0,0,-1);
        Need(f.TryVisibleResidentPursuer(resident,out seen),"Resident sight must not depend on guard facing resident.");
        foreach(var actor in f.actors.Values)actor.GetComponent<DaggerfallEntityBehaviour>().Entity.IsInvisible=true;
        Need(!f.TryVisibleResidentPursuer(resident,out seen),"Resident saw an invisible pursuing guard.");
        StreetPopulation.TestShelterHold();
        foreach(bool removed in new[]{false,true})
        {
            f=Create(out resident,out door);f.ObserveResidentBuildingEntry(resident,door,11);
            var guard=f.Roster.Guards[0];var actor=f.actors[guard.Id];var target=resident.GetComponent<DaggerfallEntityBehaviour>();
            Need(f.Roster.EnterWitnessedDoor(guard.Id,7,"door:1:7:2:3:4",20,new GuardPoint3{Y=1}),"Interior lifecycle fixture failed.");
            f.Roster.ObserveInterior(guard.Id,new GuardPoint3{Y=1},1,21,true,new GuardPoint3{X=3,Y=1});
            actor.DefenseTarget=removed?null:target;actor.CombatReady=true;
            target.Entity.CurrentHealth=0;
            f.interior=new Room{Targets=removed?new DaggerfallEntityBehaviour[0]:new[]{target}};
            f.UpdateWitnessedDoorInterior(guard,actor,22);
            Need(!actor.CombatReady && actor.DefenseTarget==null && actor.Searched,"Dead or removed target kept combat pinned instead of bounded last-seen search.");
            Need(guard.Interior.LastSeenSuspect.X==3 && guard.SearchUntil==1821,"Losing native body invented a new position or extended search.");
        }
        Console.WriteLine("Guard native adapter: 16 doorway, observer-direction, refuge-hold and target-loss scenarios passed.");return 0;
    }
}
