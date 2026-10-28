// This fixture executes the actual production classifier. Doubles provide only
// the native floor, navigation and body observations used at its API boundary.
#if NATIVE_FIXTURE
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using UnityEngine.AI;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
namespace UnityEngine
{
    public class Object {public static implicit operator bool(Object value)=>value!=null;}
    public class GameObject {public bool activeInHierarchy=true;}
    public class Component:Object
    {
        public Transform transform=new Transform();public GameObject gameObject=new GameObject();
        public List<Component> Components=new List<Component>();
        public T GetComponent<T>() where T:class=>Components.OfType<T>().FirstOrDefault();
    }
    public class Transform {public Vector3 position;public Vector3 TransformPoint(Vector3 p)=>p+position;public Vector3 InverseTransformPoint(Vector3 p)=>p-position;}
    public struct Vector3
    {
        public float x,y,z;public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
        public static Vector3 up=>new Vector3(0,1,0);public static Vector3 down=>new Vector3(0,-1,0);
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float f)=>new Vector3(a.x*f,a.y*f,a.z*f);
        public static float Distance(Vector3 a,Vector3 b){var p=a-b;return (float)Math.Sqrt(p.x*p.x+p.y*p.y+p.z*p.z);}
    }
    public class CharacterController:Component {public Vector3 center;public float height=2;}
    public class Collider {public object Parent;public T GetComponentInParent<T>() where T:class=>Parent as T;}
    public struct RaycastHit {public Vector3 point,normal;public Collider collider;}
    public enum QueryTriggerInteraction {Ignore}
    public static class Mathf {public static float Abs(float value)=>Math.Abs(value);}
    public static class Physics
    {
        public static int Calls;public static bool Lower,Unsupported,OtherBlock;
        public static bool Raycast(Vector3 origin,Vector3 direction,out RaycastHit hit,float range,int mask,QueryTriggerInteraction query)
        {
            Calls++;hit=new RaycastHit {point=new Vector3(origin.x,Lower?0:12,origin.z),normal=Vector3.up,
                collider=new Collider {Parent=Calls>1 && OtherBlock?Fixture.Other:Fixture.Block}};
            return !(Calls>1 && Unsupported);
        }
    }
}
namespace UnityEngine.AI
{
    public struct NavMeshHit {public Vector3 position;}
    public enum NavMeshPathStatus {PathComplete,PathPartial}
    public class NavMeshPath {public NavMeshPathStatus status;}
    public static class NavMesh
    {
        public const int AllAreas=-1;public static bool Complete=true;
        public static bool SamplePosition(Vector3 point,out NavMeshHit hit,float radius,int mask){hit=new NavMeshHit {position=point};return true;}
        public static bool CalculatePath(Vector3 from,Vector3 to,int mask,NavMeshPath path){path.status=Complete?NavMeshPathStatus.PathComplete:NavMeshPathStatus.PathPartial;return true;}
    }
}
namespace DaggerfallWorkshop {public class DaggerfallRMBBlock:Component{}}
namespace DaggerfallWorkshop.Game {public enum MobileTypes {Knight_CityWatch=146}}
namespace DaggerfallWorkshop.Game.Entity
{
    public class DaggerfallEntity {public int CurrentHealth=100;}
    public class EnemyEntity:DaggerfallEntity {public Mobile MobileEnemy=new Mobile();}
    public class Mobile {public int ID;}
    public class DaggerfallEntityBehaviour:Component {public DaggerfallEntity Entity=new DaggerfallEntity();}
}
public class GuardActor:Component{}
public class TownGeometry {public Vector3 ScenePoint(RoutePoint p)=>new Vector3((float)p.X,0,(float)p.Z);}
public class Directory {public Transform transform=new Transform();}
public class Site {public List<GuardPoint3> Wall=new List<GuardPoint3>();}
public class Population
{
    public GuardRoster Roster=new GuardRoster();public Directory directory=new Directory();public TownGeometry geometry=new TownGeometry();
    public HashSet<DaggerfallRMBBlock> wallWatchBlocks=new HashSet<DaggerfallRMBBlock>();public List<Site> wallWatchSites=new List<Site>();
    public double Clock=100;double Now()=>Clock;
    // ACTUAL_METHOD
}
public static class Fixture
{
    public static DaggerfallRMBBlock Block=new DaggerfallRMBBlock(),Other=new DaggerfallRMBBlock();static int checks;
    static void Need(bool value,string label){if(!value)throw new InvalidOperationException(label);checks++;Console.WriteLine("PASS "+label);}
    static Population Ready()
    {
        Physics.Calls=0;Physics.Lower=false;Physics.Unsupported=false;Physics.OtherBlock=false;NavMesh.Complete=true;
        var population=new Population();
        var points=new[]{new GuardPoint3 {X=0,Y=12},new GuardPoint3 {X=1,Y=12}};
        var journey=GuardRefugeJourney.Create(1,points,points,0,1,1);journey.WallDuty=true;journey.WallRoute=new List<GuardPoint3>(points);
        population.Roster.Guards.Add(new GuardRecord {IsAlive=true,Refuge=journey});return population;
    }
    static DaggerfallEntityBehaviour Target()
    {var target=new DaggerfallEntityBehaviour();target.transform.position=new Vector3(3,13,0);target.Components.Add(new CharacterController());return target;}
    public static int Run()
    {
        var population=Ready();Need(population.IsWallIntruder(Target()),"saved reached wall post works before transient survey caches rebuild");
        population=Ready();Physics.Lower=true;Need(!population.IsWallIntruder(Target()),"same horizontal lower street never becomes wall trespass");
        population=Ready();var guard=Target();guard.Components.Add(new GuardActor());Need(!population.IsWallIntruder(guard),"legitimate guard remains excluded");
        population=Ready();Physics.OtherBlock=true;Need(!population.IsWallIntruder(Target()),"saved post cannot classify another actual floor block");
        population=Ready();Physics.Unsupported=true;Need(!population.IsWallIntruder(Target()),"unsupported saved post cannot authorize trespass");
        population=Ready();NavMesh.Complete=false;Need(!population.IsWallIntruder(Target()),"disconnected current navigation cannot authorize trespass");
        population=Ready();population.Clock=0;Need(!population.IsWallIntruder(Target()),"journey that has not reached a post does not fabricate readiness");
        population=Ready();population.Roster.Guards.Clear();population.wallWatchBlocks.Add(Block);
        population.wallWatchSites.Add(new Site {Wall=new List<GuardPoint3>{new GuardPoint3 {Y=12}}});
        Need(population.IsWallIntruder(Target()),"existing current-survey path remains valid");
        Console.WriteLine(checks+" checks passed");return 0;
    }
}
