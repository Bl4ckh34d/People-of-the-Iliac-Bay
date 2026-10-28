using System;
using System.Collections;
using System.Collections.Generic;
public class SavedRoutePoint{public double X,Z;}
public class GuardPoint3{public double X,Y,Z;}
public enum GuardDuty{Posted,Engaged,Responding,Searching,Returning,Refuge}
public class GuardRecord
{
    public string Id="actual-guard",Shift="Night",IncidentId;public bool IsAlive=true;
    public GuardDuty Duty=GuardDuty.Posted;public object Interior;public GuardRefugeJourney Refuge;
    public double DayRestUntil=999999;public SavedRoutePoint Post=new SavedRoutePoint();
}
public static class GuardShifts{public static bool OnDuty(GuardRecord guard,double now)=>false;}
public class GuardRefugeJourney
{
    public bool HauntingShelter;public List<GuardPoint3> Exterior;
    public static GuardRefugeJourney Create(int building,List<GuardPoint3> route,object inside,double now,double speed,double scale)=>new GuardRefugeJourney{Exterior=route};
    public static GuardRefugeJourney CopyValidated(GuardRefugeJourney value)=>value;
    public bool ReleaseToReturnBy(double now)=>true;
}
public class GuardRoster
{
    public GuardRecord Guard;public double X,Z;public int Calls;public Action OnPosition;
    public bool IsOnPatrol(string id)=>false;
    public SavedRoutePoint Position(string id,double now){OnPosition?.Invoke();return new SavedRoutePoint{X=X,Z=Z};}
    public void TakeRefuge(string id,GuardRefugeJourney plan,double now,List<SavedRoutePoint> home)
    {
        Calls++;
        if(Guard.Duty==GuardDuty.Refuge || Math.Abs(plan.Exterior[0].X-X)>=.05 || Math.Abs(plan.Exterior[0].Z-Z)>=.05 ||
            Guard.Duty!=GuardDuty.Posted && (home==null || Math.Abs(home[0].X-X)>=.05 || Math.Abs(home[home.Count-1].X-Guard.Post.X)>=.05))
            throw new ArgumentException("Refuge departure requires an available guard and a matching surveyed route.");
        Guard.Refuge=plan;
    }
}
public struct Vector3{public float x,y,z;}
public class DFLocation{public enum BuildingTypes{Town23}}
public class BuildingSummary{public DFLocation.BuildingTypes BuildingType=DFLocation.BuildingTypes.Town23;}
public class Directory{public bool GetBuildingSummary(int key,out BuildingSummary value){value=new BuildingSummary();return true;}}
public class GameManager
{
    public static GameManager Instance=new GameManager();public StreamingWorld StreamingWorld=new StreamingWorld();
}
public class StreamingWorld{public object GetCurrentCityNavigation()=>null;}
public class DaggerfallUnity{public static DaggerfallUnity Instance=new DaggerfallUnity();public Clock WorldTime=new Clock();}
public class Clock{public double TimeScale=2;}
public static class Debug{public static void Log(string message){}}
public class TownGeometry
{
    public class Entrance{public int BuildingKey=7;public Vector3 LocalDoor=new Vector3{x=20};}
    public class RouteResult{public List<SavedRoutePoint> Points;}
    public Dictionary<int,Entrance> Entrances=new Dictionary<int,Entrance>{{7,new Entrance()}};
    public Action AfterRoute;public int Routes;
    public TownGeometry(){}public TownGeometry(Directory directory,object navigation,bool value){}
    public IEnumerator ReadEntranceSteps(){yield break;}
    public IEnumerator ReadRouteSteps(SavedRoutePoint from,SavedRoutePoint to,RouteResult result,Func<bool> valid)
    {
        yield return null;result.Points=new List<SavedRoutePoint>{new SavedRoutePoint{X=from.X,Z=from.Z},new SavedRoutePoint{X=to.X,Z=to.Z}};
        Routes++;AfterRoute?.Invoke();
    }
}
public class Population
{
    public GuardRoster Roster=new GuardRoster();public Directory directory=new Directory();
    public TownGeometry refugeGeometry=new TownGeometry();public bool OwnsExterior=true,Shelter=true;public double Clock;
    public object dayRestSurvey=new object();
    public Population(){Roster.Guard=new GuardRecord();}
    bool HauntingShelterNow(double now)=>Shelter;double Now()=>Clock;
    public bool CustodyEscort;bool IsCustodyEscort(GuardRecord guard)=>CustodyEscort;
    public IEnumerator Run()=>PrepareDayRest(Roster.Guard,Clock);
    // PRODUCTION_METHOD
}
public static class Fixture
{
    static int pass,fail;
    static void Drain(IEnumerator work){while(work.MoveNext())if(work.Current is IEnumerator child)Drain(child);}
    static void Check(Population population,bool commit,string name)
    {
        try{Drain(population.Run());bool good=population.Roster.Calls==(commit?1:0) && population.dayRestSurvey==null;
            if(!good)throw new Exception("calls="+population.Roster.Calls+"; handleReleased="+(population.dayRestSurvey==null));
            pass++;Console.WriteLine("PASS "+name);}
        catch(Exception error){fail++;Console.WriteLine("FAIL "+name+": "+error.Message);}
    }
    public static int Run()
    {
        Check(new Population(),true,"stable available guard commits its actual surveyed route");
        var moving=new Population();moving.refugeGeometry.AfterRoute=()=>{moving.Clock=86400+18000;moving.Roster.X=4;};
        Check(moving,false,"time jump with position drift aborts before refuge commit and releases coroutine");
        var unavailable=new Population();unavailable.refugeGeometry.AfterRoute=()=>unavailable.Roster.Guard.Duty=GuardDuty.Refuge;
        Check(unavailable,false,"changed unavailable duty cannot reach strict refuge mutation");
        var expired=new Population();expired.refugeGeometry.AfterRoute=()=>expired.Shelter=false;
        Check(expired,false,"ended haunting shelter releases pending plan");
        var post=new Population();post.Roster.Guard.Duty=GuardDuty.Engaged;
        post.refugeGeometry.AfterRoute=()=>{if(post.refugeGeometry.Routes==2)post.Roster.Guard.Post.X=9;};
        Check(post,false,"changed actual return post invalidates surveyed combat-home route");
        var advance=new Population();advance.refugeGeometry.AfterRoute=()=>advance.Roster.OnPosition=()=>advance.Roster.X=3;
        Check(advance,false,"final roster advance cannot silently change departure origin");
        var escort=new Population();escort.refugeGeometry.AfterRoute=()=>escort.CustodyEscort=true;
        Check(escort,false,"actual custody escort interrupts pending shelter before refuge mutation");
        Console.WriteLine(pass+" passed; "+fail+" failed");return fail==0?0:1;
    }
}
