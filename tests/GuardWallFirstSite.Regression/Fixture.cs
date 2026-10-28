using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
public struct Vector3
{
    public float x,y,z;
    public Vector3(float x,float y,float z){this.x=x;this.y=y;this.z=z;}
}
public class Transform
{
    public Vector3 position,rotation;
    public Vector3 InverseTransformPoint(Vector3 point)=>point;
}
public enum DoorTypes {Building}
public class DFLocation {public enum BuildingTypes {Town23}}
public class BuildingSummary {public DFLocation.BuildingTypes BuildingType;}
public class DaggerfallRMBBlock {public int Id;public static implicit operator bool(DaggerfallRMBBlock block)=>block!=null;}
public struct StaticDoor
{
    public DoorTypes doorType;public int buildingKey,blockIndex,recordIndex,doorIndex;
    public Vector3 ownerPosition,ownerRotation,Point;
}
public class DaggerfallStaticDoors
{
    public Transform transform=new Transform();public StaticDoor[] Doors;public DaggerfallRMBBlock Block;
    public T GetComponentInParent<T>() where T:class=>Block as T;
    public static Vector3 GetDoorPosition(StaticDoor door)=>door.Point;
    public static Vector3 GetDoorNormal(StaticDoor door)=>new Vector3();
}
public class Directory
{
    public Transform transform=new Transform();public List<DaggerfallStaticDoors> Owners=new List<DaggerfallStaticDoors>();
    public T[] GetComponentsInChildren<T>() where T:class=>Owners.Cast<T>().ToArray();
    public bool GetBuildingSummary(int key,out BuildingSummary summary){summary=new BuildingSummary();return true;}
}
public struct NavMeshHit {public Vector3 position;}
public struct RoutePoint {public RoutePoint(double x,double z){}}
public class SavedRoutePoint {public double X,Z;}
public class GuardPoint3 {public double X,Y,Z;}
public static class GuardRoster {public const int MaximumWallPosts=30;}
public static class Debug {public static void Log(string message){}}
public class TownGeometry
{
    public class RouteResult {public List<SavedRoutePoint> Points;}
    public Vector3 ScenePoint(RoutePoint point)=>new Vector3();
    public IEnumerator ReadRouteSteps(SavedRoutePoint from,SavedRoutePoint to,RouteResult result,Func<bool> valid)
    {result.Points=new List<SavedRoutePoint>{from,to};yield break;}
}
public class Population
{
    class WallDoor {public DaggerfallStaticDoors Owner;public StaticDoor Door;public Vector3 Position,Normal;public float Rise;}
    class WallWatchSite {public int BuildingKey,LowerDoorIndex,UpperDoorIndex;public SavedRoutePoint LowerPoint;public List<GuardPoint3> Interior,Wall;}
    class GatePost {public SavedRoutePoint Point=new SavedRoutePoint();}
    readonly Directory directory=new Directory();readonly TownGeometry geometry=new TownGeometry();
    readonly List<WallWatchSite> wallWatchSites=new List<WallWatchSite>();
    readonly HashSet<DaggerfallRMBBlock> wallWatchBlocks=new HashSet<DaggerfallRMBBlock>();
    readonly List<GatePost> gateDutyPosts=new List<GatePost>{new GatePost()};
    public readonly List<int> Surveyed=new List<int>();public readonly List<int> AtFirstAssignment=new List<int>();
    public bool Valid=true,FailFirstStreet;public int Assignments;
    public Population()
    {
        for(int block=0;block<3;block++)
        {
            var owner=new DaggerfallStaticDoors {Block=new DaggerfallRMBBlock{Id=block}};
            owner.Doors=new[]{new StaticDoor{buildingKey=block,doorIndex=0,Point=new Vector3(block*50,0,0)},
                new StaticDoor{buildingKey=block,doorIndex=1,Point=new Vector3(block*50,5,0)},
                new StaticDoor{buildingKey=block+10,doorIndex=0,Point=new Vector3(block*50,0,0)},
                new StaticDoor{buildingKey=block+10,doorIndex=1,Point=new Vector3(block*50,5,0)}};
            directory.Owners.Add(owner);
        }
    }
    public IEnumerator Run()=>SurveyWallWatchSites(()=>Valid);
    IEnumerator SurveyWallSurfaces(HashSet<DaggerfallRMBBlock> blocks,List<Vector3> result,Func<bool> valid)
    {foreach(var block in blocks){Surveyed.Add(block.Id);yield return null;if(!valid())yield break;result.Add(new Vector3(block.Id*50,5,0));}}
    IEnumerator FindWallPostRoutes(Vector3 entry,List<Vector3> surfaces,List<List<GuardPoint3>> result,Func<bool> valid)
    {result.Add(new List<GuardPoint3>{new GuardPoint3(),new GuardPoint3 {X=entry.x+4,Y=5}});yield break;}
    static IEnumerator SurveyWallInterior(WallDoor lower,WallDoor upper,List<GuardPoint3> result,Func<bool> valid)
    {result.Add(new GuardPoint3());result.Add(new GuardPoint3 {Y=5});yield break;}
    IEnumerator AssignReadyWallWatch(Func<bool> valid)
    {if(Assignments++==0)AtFirstAssignment.AddRange(Surveyed);yield break;}
    static bool DoorLanding(WallDoor door,out NavMeshHit hit){hit=new NavMeshHit {position=door.Position};return true;}
    bool RouteCrossesGate(List<SavedRoutePoint> route)=>FailFirstStreet && route.Last().X==0;
    // ACTUAL_SURVEY
}
public static class Fixture
{
    static int checks;
    static void Drain(IEnumerator work){while(work.MoveNext())if(work.Current is IEnumerator child)Drain(child);}
    static void Need(bool value,string text){if(!value)throw new InvalidOperationException(text);checks++;Console.WriteLine("PASS "+text);}
    public static int Run()
    {
        var population=new Population();Drain(population.Run());
        Need(population.AtFirstAssignment.SequenceEqual(new[]{0}),"first actual ready site publishes before surveying unrelated wall blocks");
        Need(population.Surveyed.OrderBy(value=>value).SequenceEqual(new[]{0,1,2}) && population.Assignments==6,
            "later real blocks and access sites still publish through the existing survey path");
        Need(population.Surveyed.Distinct().Count()==population.Surveyed.Count,"same real block is not resurveyed for another access door");
        population=new Population {FailFirstStreet=true};Drain(population.Run());
        Need(population.AtFirstAssignment.SequenceEqual(new[]{1}),"invalid street approach does not demand unrelated first-block surface work");
        population=new Population {Valid=false};Drain(population.Run());
        Need(population.Surveyed.Count==0 && population.Assignments==0,"cancelled native survey publishes no site and surveys no blocks");
        Console.WriteLine(checks+" checks passed");return 0;
    }
}
