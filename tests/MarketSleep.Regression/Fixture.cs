using System;

public struct Vector3 {public static Vector3 operator-(Vector3 a,Vector3 b)=>new Vector3();}
public sealed class Transform {public Vector3 position;}
public sealed class Interior {public Transform transform=new Transform();}
public sealed class NativePerson {}
public sealed class NativeMarketCommuter
{public Transform transform=new Transform();public GameObject gameObject=new GameObject();public Vector3 Offset;public object Route;public NativePerson Person=new NativePerson();public string Phase="to-bed";}
public sealed class GameObject {public T AddComponent<T>() where T:new()=>new T();}
public sealed class SleepingArt
{
    public sealed class Atlas {}
    public Atlas Result;public bool Throws;
    public Atlas ForNative(NativePerson person){if(Throws)throw new InvalidOperationException("Missing art");return Result;}
}
public sealed class SleepingPose
{public bool Active;public static implicit operator bool(SleepingPose value)=>value!=null;public void BeginNative(Transform root,object bed,SleepingArt.Atlas art){Active=true;}}
public static class ResidentMovement {public const float WalkSpeed=1;}
public sealed class Fixture
{
    private bool SleepEnabled=true,arrived=true;private SleepingArt sleepingArt;private Interior interior=new Interior();
    private bool MarketMove(NativeMarketCommuter body,Vector3 target,object route,float speed)=>arrived;
    private object MarketIndoorRoute(Vector3 from,Vector3 to)=>new object();
    private void MarketPhase(NativeMarketCommuter body,string phase,double now,string fact)=>body.Phase=phase;
    private void Enter(NativeMarketCommuter body,bool sleep)
    {
        var person=body.Person;var beside=new Vector3();object bed=new object();double now=100;
        SleepingPose sleeping=null;
        // ARRIVAL_BLOCK
    }
    static int checks,failures;static void Check(bool value,string message){checks++;if(!value){failures++;Console.WriteLine("FAIL "+message);}else Console.WriteLine("PASS "+message);}
    public static int Run()
    {
        var f=new Fixture();var body=new NativeMarketCommuter();
        f.Enter(body,true);Check(body.Phase=="sleep","reached assigned bed permits sleep when native art is absent");
        body.Phase="to-bed";f.SleepEnabled=false;f.sleepingArt=new SleepingArt{Result=new SleepingArt.Atlas()};
        f.Enter(body,true);Check(body.Phase=="sleep","disabled sleep art does not disable reached-bed sleep");
        body.Phase="to-bed";f.SleepEnabled=true;f.sleepingArt=new SleepingArt();
        f.Enter(body,true);Check(body.Phase=="sleep","missing native atlas does not leave reached sleeper walking to bed");
        body.Phase="to-bed";f.sleepingArt=new SleepingArt{Throws=true};
        try {f.Enter(body,true);Check(body.Phase=="sleep","unloadable native atlas retains reached-bed sleep");}
        catch {Check(false,"unloadable native atlas retains reached-bed sleep");}
        body.Phase="to-bed";f.sleepingArt=new SleepingArt{Result=new SleepingArt.Atlas()};f.Enter(body,true);
        Check(body.Phase=="sleep","available native art still permits sleep");
        f.arrived=false;f.Enter(body,true);Check(body.Phase=="to-bed","unreached bed never becomes sleep even with art");
        f.arrived=true;f.Enter(body,false);Check(body.Phase=="home","awake reached-bed resident remains home");
        Console.WriteLine((checks-failures)+"/"+checks+" passed");return failures==0?0:1;
    }
}
