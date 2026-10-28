using System;
public struct Vector3
{
    public float x,y,z;public Vector3(float a,float b,float c){x=a;y=b;z=c;}
    public static Vector3 up=>new Vector3(0,1,0);public static Vector3 down=>new Vector3(0,-1,0);
    public float sqrMagnitude=>x*x+y*y+z*z;
    public static Vector3 operator +(Vector3 a,Vector3 b)=>new Vector3(a.x+b.x,a.y+b.y,a.z+b.z);
    public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3(a.x-b.x,a.y-b.y,a.z-b.z);
    public static Vector3 operator *(Vector3 a,float b)=>new Vector3(a.x*b,a.y*b,a.z*b);
    public static float Distance(Vector3 a,Vector3 b)=>(float)Math.Sqrt((a-b).sqrMagnitude);
    public static Vector3 MoveTowards(Vector3 a,Vector3 b,float distance){float gap=Distance(a,b);return gap<=distance?b:a+(b-a)*(distance/gap);}
    public Vector3 normalized{get{float length=Distance(this,new Vector3());return length==0?this:this*(1/length);}}
}
public static class Mathf{public static float Abs(float value)=>Math.Abs(value);public static float Sqrt(float value)=>(float)Math.Sqrt(value);}
public class Transform{public Vector3 position=new Vector3(4,9,0),forward;}
public class Entity{public bool IsParalyzed;}
public static class GameManager{public static bool IsGamePaused;}
public static class Time{public static float time=10,deltaTime=.1f;}
public enum MobileStates{Idle,Move}
public class MobileUnit{public MobileStates EnemyState;public void ChangeEnemyState(MobileStates value){EnemyState=value;}}
public class CharacterController
{
    public float radius=.5f;public bool Blocked;public int Moves;public Transform Root;
    public void Move(Vector3 step){Moves++;if(!Blocked)Root.position+=step;}
}
public struct NavMeshHit{public Vector3 position;}
public enum NavMeshPathStatus{PathComplete,PathInvalid}
public class NavMeshPath{public Vector3[] corners;public NavMeshPathStatus status;}
public static class NavMesh
{
    public const int AllAreas=-1;public static float StartHeightOffset;public static bool LowerCorner,Edge,Complete=true;
    public static bool SamplePosition(Vector3 value,out NavMeshHit hit,float radius,int areas)
    {hit=new NavMeshHit{position=value+Vector3.up*(value.x>1?StartHeightOffset:0)};return true;}
    public static bool CalculatePath(Vector3 from,Vector3 to,int areas,NavMeshPath path)
    {path.status=Complete?NavMeshPathStatus.PathComplete:NavMeshPathStatus.PathInvalid;path.corners=LowerCorner?new[]{from,new Vector3(2,0,0),to}:new[]{from,to};return Complete;}
    public static bool Raycast(Vector3 from,Vector3 to,out NavMeshHit hit,int areas){hit=new NavMeshHit();return Edge;}
    public static void Reset(){StartHeightOffset=0;LowerCorner=false;Edge=false;Complete=true;}
}
public struct Ray{public Vector3 origin,direction;public Ray(Vector3 point,Vector3 heading){origin=point;direction=heading;}}
public struct RaycastHit{}
public static class Physics
{
    public static float Floor=8;public static Ray LastRay;public static float LastRange;
    public static bool Raycast(Ray ray,out RaycastHit hit,float range){LastRay=ray;LastRange=range;hit=new RaycastHit();return ray.origin.y>=Floor && ray.origin.y-Floor<=range;}
}
public class Actor
{
    public Transform transform=new Transform();public CharacterController Controller;public Entity entity=new Entity();
    public MobileUnit mobile=new MobileUnit();public float groundOffset=1;
    public bool wallCombatDisplaced=true;public Vector3[] wallReturnCorners;public int wallReturnIndex;
    public Vector3 wallReturnDestination;public float wallReturnRepath;
    public Actor(){Controller=new CharacterController{Root=transform};}
    public Vector3 GroundPosition=>transform.position-Vector3.up*groundOffset;
    public T GetComponent<T>() where T:class=>Controller as T;
    public bool Return(Vector3 target)=>ReturnToWallPost(target);
    // RETURN_METHOD
}
public class NativeMotor
{
    bool flies,IsLevitating,swims,ObstacleDetected,foundUpwardSlope,foundDoor;
    public bool fallDetected;public float originalHeight=2;public Transform transform=new Transform();
    public CharacterController controller=new CharacterController();
    public void Check(Vector3 direction){FallCheck(direction);}
    public float RadiusDistance(){ // NATIVE_RADIUS
        return checkDistance;}
    // FALL_METHOD
}
public static class Fixture
{
    static int passed;static void Need(bool value,string name){if(!value)throw new InvalidOperationException(name);passed++;Console.WriteLine("PASS "+name);}
    public static int Run()
    {
        var target=new Vector3(0,9,0);NavMesh.Reset();var actor=new Actor();var start=actor.transform.position;
        Need(actor.Return(target) && Vector3.Distance(actor.transform.position,start)>.01f && Vector3.Distance(actor.transform.position,start)<.14f,
            "post return uses one bounded native controller step on elevated NavMesh");
        Need(actor.transform.position.y==9,"post return preserves actual upper-floor body height");
        actor=new Actor();actor.wallCombatDisplaced=false;Need(!actor.Return(target) && actor.Controller.Moves==0,"ordinary noncombat post is unchanged");
        actor=new Actor();NavMesh.StartHeightOffset=-8;Need(actor.Return(target) && actor.Controller.Moves==0,"street projection is rejected before movement");
        NavMesh.Reset();actor=new Actor();NavMesh.LowerCorner=true;Need(actor.Return(target) && actor.Controller.Moves==0,"return route through lower street is rejected");
        NavMesh.Reset();actor=new Actor();NavMesh.Complete=false;Need(actor.Return(target) && actor.Controller.Moves==0,"incomplete native return path never snaps to post");
        NavMesh.Reset();actor=new Actor();NavMesh.Edge=true;Need(actor.Return(target) && actor.Controller.Moves==0,"native NavMesh edge blocks return step");
        NavMesh.Reset();actor=new Actor();actor.Controller.Blocked=true;start=actor.transform.position;
        actor.Return(target);Need(Vector3.Distance(start,actor.transform.position)==0,"real controller collision remains movement authority");
        actor=new Actor();actor.entity.IsParalyzed=true;Need(actor.Return(target) && actor.Controller.Moves==0,"paralysis cannot be bypassed by post return");
        actor=new Actor();actor.transform.position=target;Need(!actor.Return(target) && !actor.wallCombatDisplaced,"actual reached post releases temporary return state");
        var motor=new NativeMotor();Physics.Floor=8;motor.Check(new Vector3(1,0,0));
        Need(!motor.fallDetected,"actual native FallCheck admits supported upper-wall movement");
        Physics.Floor=0;motor.Check(new Vector3(1,0,0));Need(motor.fallDetected,"actual native FallCheck rejects eight-metre drop to street");
        Need(Physics.LastRay.origin.x==5 && Physics.LastRange==2.5f,"native cliff probe checks one metre ahead with actual height-based range");
        Need(Math.Abs(motor.RadiusDistance()-.5f/(float)Math.Sqrt(2))<.00001f,"native obstacle check scales its lookahead to actual controller radius");
        Console.WriteLine(passed+" checks passed");return 0;
    }
}
