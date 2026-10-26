using System;
using System.Collections.Generic;

public struct Vector3
{
    public float x,y,z;
    public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
    public static Vector3 zero => new Vector3();
    public static float Distance(Vector3 a,Vector3 b) => (float)Math.Sqrt((a.x-b.x)*(a.x-b.x)+(a.y-b.y)*(a.y-b.y)+(a.z-b.z)*(a.z-b.z));
}
public enum NpcActionKind {Enter,Exit}
public sealed class NpcActionTarget {public int BuildingKey;public string DoorId;}
public sealed class NpcActionJob {public NpcActionKind Kind;public NpcActionTarget Target;}
public sealed class ResidentActor {public static bool operator !(ResidentActor actor)=>actor==null;}
public static class NpcTaskContract
{public static bool TargetMatches(NpcActionTarget a,NpcActionTarget b)=>a.BuildingKey==b.BuildingKey && a.DoorId==b.DoorId;}
public struct RoutePoint {public double X,Z;public RoutePoint(double x,double z){X=x;Z=z;}}
public sealed class TownGeometry
{
    public sealed class Entrance {public Vector3 LocalDoor;}
    public Dictionary<int,Entrance> Entrances=new Dictionary<int,Entrance>();
    public float GroundHeight=4;
    public Vector3 ScenePoint(RoutePoint point)=>new Vector3((float)point.X,GroundHeight,(float)point.Z);
}
public sealed class Directory {public Transform transform=new Transform();}
public sealed class Transform {public Vector3 InverseTransformPoint(Vector3 point)=>point;}
public struct StaticDoor {public Vector3 Ground;}
public sealed class IndoorGeometry {public Vector3? EntryGround=new Vector3(2,3,4);}

public sealed class Fixture
{
    private TownGeometry geometry=new TownGeometry();
    private Directory directory=new Directory();
    private IndoorGeometry indoorGeometry=new IndoorGeometry();
    private bool available=true;
    private StaticDoor acceptedDoor=new StaticDoor {Ground=new Vector3(20,4,30)};
    private static int failures;
    private bool TryBuildingActionTarget(ResidentActor actor,NpcActionKind kind,int key,string doorId,out NpcActionTarget target)
    {target=new NpcActionTarget {BuildingKey=key,DoorId=doorId};return available;}
    private bool ReadBuildingActionDoor(int key,string expectedId,out StaticDoor door)
    {door=acceptedDoor;return available && key==88 && expectedId=="bank:accepted-door";}
    private static Vector3 FollowExteriorDoorGround(StaticDoor door)=>door.Ground;

    // PRODUCTION_METHOD

    private static void Check(bool passed,string message)
    {if(!passed){failures++;Console.WriteLine("FAIL "+message);}}
    public static int Run()
    {
        var fixture=new Fixture();
        fixture.geometry.Entrances[88]=new TownGeometry.Entrance {LocalDoor=new Vector3(100,4,100)};
        var job=new NpcActionJob {Kind=NpcActionKind.Enter,Target=new NpcActionTarget {BuildingKey=88,DoorId="bank:accepted-door"}};
        Vector3 target;string reason;
        Check(fixture.ResolveBuildingAction(job,new ResidentActor(),out target,out reason) && Vector3.Distance(target,fixture.acceptedDoor.Ground)<0.001f,
            "Accepted bank door must resolve its own ground, not another default entrance.");
        fixture.geometry.Entrances[88].LocalDoor=new Vector3(200,4,200);
        Check(fixture.ResolveBuildingAction(job,new ResidentActor(),out target,out reason) && Vector3.Distance(target,fixture.acceptedDoor.Ground)<0.001f,
            "Changing default entrance must not move an accepted exact-door target.");
        fixture.geometry.GroundHeight=6;
        Check(!fixture.ResolveBuildingAction(job,new ResidentActor(),out target,out reason),
            "Door above surveyed terrain must reject rather than invent a reachable landing.");
        fixture.geometry.GroundHeight=4.2f;
        Check(fixture.ResolveBuildingAction(job,new ResidentActor(),out target,out reason) && Math.Abs(target.y-4.2f)<0.001f,
            "Small floor difference keeps the existing surveyed terrain endpoint.");
        fixture.available=false;
        Check(!fixture.ResolveBuildingAction(job,new ResidentActor(),out target,out reason),"Unavailable or locked door must reject.");
        fixture.available=true;job.Kind=NpcActionKind.Exit;
        Check(fixture.ResolveBuildingAction(job,new ResidentActor(),out target,out reason) && Vector3.Distance(target,fixture.indoorGeometry.EntryGround.Value)<0.001f,
            "Exit must retain the observed native interior entry ground.");
        return failures;
    }
}
