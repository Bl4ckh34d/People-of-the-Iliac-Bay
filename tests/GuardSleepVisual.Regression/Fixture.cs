using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.DFU;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game;

namespace UnityEngine
{
    public class Object {public static implicit operator bool(Object value)=>value!=null;public static T Instantiate<T>(T value)=>value;public static void Destroy(Object value) {}}
    public class Component:Object
    {
        public GameObject gameObject;public Transform transform=>gameObject.transform;
        public T GetComponent<T>() where T:Component=>gameObject.GetComponent<T>();
        public T GetComponentInChildren<T>() where T:Component=>gameObject.GetComponentsInChildren<T>().FirstOrDefault();
        public T[] GetComponentsInChildren<T>(bool includeInactive=false) where T:Component=>gameObject.GetComponentsInChildren<T>(includeInactive);
    }
    public class MonoBehaviour:Component {public bool enabled=true;public object StartCoroutine(System.Collections.IEnumerator value)=>value;}
    public class GameObject:Object
    {
        public string name;public int layer;public bool activeSelf=true;public Transform transform;
        readonly List<Component> components=new List<Component>();
        public GameObject(){transform=new Transform{gameObject=this};components.Add(transform);}
        public T AddComponent<T>() where T:Component,new(){var result=new T{gameObject=this};components.Add(result);return result;}
        public T GetComponent<T>() where T:Component=>components.OfType<T>().FirstOrDefault();
        public T GetComponentInChildren<T>() where T:Component=>GetComponentsInChildren<T>().FirstOrDefault();
        public T[] GetComponentsInChildren<T>(bool includeInactive=false) where T:Component=>components.OfType<T>().Concat(transform.Children.Where(c=>includeInactive||c.gameObject.activeSelf).SelectMany(c=>c.gameObject.GetComponentsInChildren<T>(includeInactive))).ToArray();
        public void SetActive(bool value){activeSelf=value;}
        public static GameObject CreatePrimitive(PrimitiveType type){var o=new GameObject();o.AddComponent<Collider>();o.AddComponent<MeshFilter>().sharedMesh=new Mesh();o.AddComponent<MeshRenderer>();return o;}
    }
    public class Transform:Component
    {
        public List<Transform> Children=new List<Transform>();public Transform parent;public Vector3 position,localPosition,localScale,forward;public Quaternion rotation,localRotation;
        public void SetParent(Transform value,bool world=true){parent=value;value.Children.Add(this);}
        public Vector3 TransformPoint(Vector3 p)=>position+p;public Vector3 InverseTransformPoint(Vector3 p)=>p-position;public Vector3 TransformDirection(Vector3 p)=>p;
    }
    public struct Vector3
    {
        public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}
        public static Vector3 zero=>new Vector3();public static Vector3 up=>new Vector3(0,1,0);
        public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
        public static Vector3 operator *(Vector3 a,float b)=>new Vector3(a.x*b,a.y*b,a.z*b);
    }
    public struct Vector2 {public float x,y;public Vector2(float a,float b){x=a;y=b;}}
    public struct Quaternion {public static Quaternion LookRotation(Vector3 p)=>new Quaternion();public static Quaternion Euler(float x,float y,float z)=>new Quaternion();}
    public struct Rect {public float xMin,xMax,yMin,yMax,width,height;}
    public class Renderer:Component {public bool enabled=true;}
    public class MeshRenderer:Renderer {public Material sharedMaterial;public Rendering.ShadowCastingMode shadowCastingMode;}
    public class MeshFilter:Component {public Mesh sharedMesh;}
    public class Mesh:Object {public Vector2[] uv=new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(1,1),new Vector2(0,1)};}
    public class Material:Object {public object mainTexture;public Material(object shader){}}
    public class Shader {public static object Find(string name)=>new object();}
    public class Texture2D:Object {public int width=100,height=100;}
    public class Collider:Component {public bool enabled=true;}
    public class CapsuleCollider:Collider {public float height,radius;public int direction;public Vector3 center;}
    public class BoxCollider:Collider {public Vector3 size,center;}
    public class CharacterController:Collider {public float height,radius;public Vector3 center;}
    public static class Mathf {public static float Min(float a,float b)=>Math.Min(a,b);public static float Lerp(float a,float b,float t)=>a+(b-a)*t;}
    public enum PrimitiveType {Quad}
    [AttributeUsage(AttributeTargets.Class,AllowMultiple=true)] public class RequireComponent:Attribute {public RequireComponent(Type type){}}
    public static class Debug {public static void Log(string message){}public static void LogWarning(string message){}}
}
namespace UnityEngine.Rendering {public enum ShadowCastingMode {Off}}
namespace DaggerfallWorkshop
{
    public class MobileUnit:MonoBehaviour {}
    public class DaggerfallInterior:Component {}
    public class DaggerfallBillboard:Component {public SummaryData Summary=new SummaryData();public class SummaryData {public Vector3 Size;}}
}
namespace DaggerfallWorkshop.Game.MagicAndEffects {public class EntityEffectManager:Component {}}
namespace DaggerfallWorkshop.Game {public class GameManager {public static GameManager Instance=new GameManager();public Component PlayerMotor;}}
namespace DaggerfallWorkshop.Game.Entity
{
    public class DaggerfallEntity {public bool IsMagicallyConcealed;}
    public class DaggerfallEntityBehaviour:Component {public DaggerfallEntity Entity=new DaggerfallEntity();}
}
namespace PeopleOfTheIliacBay.Core
{
    public class BedAnchor {public int RecordIndex;public float Length=2,Width=1;public Vector3 LocalCenter=new Vector3(9,2,3),LocalHeadDirection;}
    public enum GuardDuty {Posted,Engaged}
}
namespace PeopleOfTheIliacBay.DFU
{
    public class ResidentActor:MonoBehaviour {public PersonData Person=new PersonData();public void Place(Vector3 p,Vector3 f,bool b){}public class PersonData {public Component Asset;}}
    public class InteriorGeometry {public bool StandBesideBed(BedAnchor b,out Vector3 p){p=Vector3.zero;return true;}}
    public class SleepingArt {public Atlas Result;public bool Throws;public Atlas ForCityWatch(){if(Throws)throw new InvalidOperationException("Sleeping atlas missing: fixture");return Result;}public class Atlas {public Texture2D Texture;public Rect[] Views;}}
    public static class BedGeometry {public static List<BedAnchor> Read(DaggerfallInterior room)=>new List<BedAnchor>();}
    public class QuarterBed:Component {public BedAnchor Anchor;}
    public class GuardRecord {public string Id="test";public GuardDuty Duty;public InteriorRecord Interior;public RefugeRecord Refuge=new RefugeRecord();public SavedRoutePoint EngagementPosition,LastSeenSuspect;public double HealthFraction=1;}
    public class InteriorRecord {public object Position,LastSeenSuspect;public bool Returning;}
    public class RefugeRecord {public int? RestBedRecord=7;}
    public class SavedRoutePoint {public double X,Z;}
    public class Roster {public List<GuardRecord> Guards=new List<GuardRecord>{new GuardRecord()};public void Kill(string id,double time){}public void Engage(string id,double now){Guards[0].Duty=GuardDuty.Engaged;}public void ObserveInterior(string id,object p,double health,double now,bool sees,object target){}}
    public class GuardActor:MonoBehaviour
    {
        public SleepingPose sleeping;public SleepingArt sleepArt;public BedAnchor restBed;public string GuardId="test";
        private Roster roster=new Roster();private bool IsInterior=true;private double displayedAt=123;
        private object combatStart;public bool CombatReady;public DaggerfallEntityBehaviour DefenseTarget;
        private void EndCombat(){DefenseTarget=null;}private bool EnsureNativeTargetController(DaggerfallEntityBehaviour target)=>true;
        private void CaptureDefense(double now){}private System.Collections.IEnumerator StartCombat(){yield break;}private System.Collections.IEnumerator StartDefenseCombat(){yield break;}
        private static object Point(Vector3 value)=>value;private void SetTorch(bool a,bool b,float c){}
        public void Rest(){var guard=roster.Guards[0];bool indoors=true,atRestBed=true;
            // REST_BLOCK
        }
        // DIED_METHOD
        // COMBAT_METHODS
        public void Die()=>Died(new DaggerfallEntity());
    }
}
public static class Fixture
{
    static int checks,failures;
    static void Check(bool pass,string name){checks++;Console.WriteLine((pass?"PASS ":"FAIL ")+name);if(!pass)failures++;}
    static void Call(object target,string name)=>target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,null);
    static (GuardActor guard,MeshRenderer standing,CharacterController body,EntityConcealmentBehaviour conceal) Create()
    {
        var room=new GameObject();room.AddComponent<DaggerfallInterior>();var root=new GameObject();root.transform.SetParent(room.transform);
        var sprite=new GameObject();sprite.transform.SetParent(root.transform);sprite.AddComponent<MobileUnit>();var standing=sprite.AddComponent<MeshRenderer>();
        var body=root.AddComponent<CharacterController>();body.height=2;body.radius=.5f;body.center=new Vector3(0,1,0);
        root.AddComponent<DaggerfallEntityBehaviour>();root.AddComponent<DaggerfallWorkshop.Game.MagicAndEffects.EntityEffectManager>();
        var conceal=root.AddComponent<EntityConcealmentBehaviour>();Call(conceal,"Awake");
        var guard=root.AddComponent<GuardActor>();guard.restBed=new BedAnchor{RecordIndex=7};
        guard.sleepArt=new SleepingArt{Result=new SleepingArt.Atlas{Texture=new Texture2D(),Views=new[]{new Rect{width=1,height=1,xMax=1,yMax=1}}}};
        return (guard,standing,body,conceal);
    }
    public static int Run()
    {
        var f=Create();var identity=f.guard.gameObject.GetComponent<DaggerfallEntityBehaviour>();f.guard.Rest();
        Check(f.guard.sleeping.Active&&!f.standing.enabled,"valid sleeping art hides native standing sprite");
        for(int frame=0;frame<3;frame++){Call(f.conceal,"Update");Call(f.guard.sleeping,"LateUpdate");}
        Check(!f.standing.enabled,"sleep owns standing visibility after native concealment Update");
        Check(f.guard.gameObject.GetComponentsInChildren<Renderer>().Count(r=>r.enabled&&r.gameObject.activeSelf)==1,"exactly one rendered guard visual");
        Check(identity==f.guard.gameObject.GetComponent<DaggerfallEntityBehaviour>()&&identity.gameObject.activeSelf&&f.body.enabled,"sleep retains native identity and collision");
        Check(f.guard.gameObject.GetComponentInChildren<MobileUnit>().enabled&&f.conceal.enabled,"sleep retains native animation and concealment owners");
        f.guard.sleeping.Wake();Check(f.standing.enabled&&f.body.height==2&&f.body.radius==.5f&&f.body.center.y==1,"wake restores native renderer and controller dimensions");
        Check(f.guard.gameObject.GetComponentsInChildren<Renderer>().Count(r=>r.enabled&&r.gameObject.activeSelf)==1,"wake leaves exactly one visual");
        f.guard.Rest();Call(f.guard.sleeping,"OnDisable");Check(!f.guard.sleeping.Active&&f.standing.enabled&&f.body.height==2,"scene disable restores standing state");
        f.guard.Rest();f.guard.Die();Check(!f.guard.sleeping.Active&&f.standing.enabled&&f.body.height==2,"death releases sleeping visual before native death handling");
        f=Create();f.standing.enabled=false;f.guard.Rest();f.guard.sleeping.Wake();Check(!f.standing.enabled,"wake preserves previously hidden renderer");
        f=Create();f.guard.Rest();DaggerfallWorkshop.Game.GameManager.Instance.PlayerMotor=f.guard;
        f.guard.BeginCombat(123);Check(!f.guard.sleeping.Active&&f.standing.enabled&&f.body.height==2,"player combat interruption restores native standing body");
        f=Create();f.guard.Rest();f.guard.BeginDefenseCombat(f.guard.GetComponent<DaggerfallEntityBehaviour>(),123);
        Check(!f.guard.sleeping.Active&&f.standing.enabled&&f.body.height==2,"enemy defense interruption restores native standing body");
        f=Create();f.guard.sleepArt.Result=null;f.guard.Rest();Check(!f.guard.sleeping&&f.standing.enabled&&f.body.height==2,"missing optional art retains standing rest body");
        f=Create();f.guard.sleepArt.Throws=true;try{f.guard.Rest();Check(!f.guard.sleeping&&f.standing.enabled,"invalid optional art cannot interrupt rest placement");}catch{Check(false,"invalid optional art cannot interrupt rest placement");}
        Console.WriteLine($"Guard sleep visual: {checks-failures}/{checks} checks passed.");return failures==0?0:1;
    }
}
