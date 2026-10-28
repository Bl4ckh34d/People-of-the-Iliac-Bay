using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using PeopleOfTheIliacBay.Core;
public class DaggerfallUnity {public static DaggerfallUnity Instance=new DaggerfallUnity();public Clock WorldTime=new Clock();}
public class Clock {public double TimeScale=2;}
public static class Debug {public static void Log(string value){}public static void LogWarning(string value){}}
public sealed class Site
{
    public int BuildingKey=512,LowerDoorIndex=2,UpperDoorIndex=0;public SavedRoutePoint LowerPoint=new SavedRoutePoint {X=8};
    public List<GuardPoint3> Interior=new List<GuardPoint3>{new GuardPoint3 {Y=1},new GuardPoint3 {Y=4}};
    public List<GuardPoint3> Wall=new List<GuardPoint3>{new GuardPoint3 {X=8,Y=4},new GuardPoint3 {X=10,Y=4}};
}
public sealed class TownGeometry
{
    public sealed class RouteResult {public List<SavedRoutePoint> Points;}
    public bool Blocked;public Action AfterRoute;public int Queries;
    public IEnumerator ReadRouteSteps(SavedRoutePoint from,SavedRoutePoint to,RouteResult result,Func<bool> valid)
    {
        Queries++;yield return null;
        result.Points=Blocked?new List<SavedRoutePoint>():new List<SavedRoutePoint>{
            new SavedRoutePoint {X=from.X,Z=from.Z},new SavedRoutePoint {X=to.X,Z=to.Z}};
        AfterRoute?.Invoke();
    }
}
public sealed class Population
{
    public GuardRoster Roster=GuardRoster.Create(1,42,4,new[]{new SavedRoutePoint(),new SavedRoutePoint {X=2},new SavedRoutePoint {X=4},new SavedRoutePoint {X=10}});
    public List<Site> wallWatchSites=new List<Site>{new Site()};public TownGeometry geometry=new TownGeometry();
    public List<PatrolPairPlan> patrolTemplates=new List<PatrolPairPlan>();public HashSet<int> wallAssignmentFailures=new HashSet<int>();
    public HashSet<string> Gates=new HashSet<string>();public bool Valid=true,SurveyComplete;
    private bool IsGateGuard(string id)=>Gates.Contains(id);private double Now()=>72000;
    public IEnumerator Run()=>AssignReadyWallWatch(()=>Valid);
    // ACTUAL_GAP
    // ACTUAL_ASSIGNMENT
}
public static class Fixture
{
    static int checks;
    static void Drain(IEnumerator work){while(work.MoveNext())if(work.Current is IEnumerator child)Drain(child);}
    static void Need(bool value,string text){if(!value)throw new InvalidOperationException(text);checks++;Console.WriteLine("PASS "+text);}
    public static int Run()
    {
        var population=new Population();var ids=population.Roster.Guards.Select(value=>value.Id).ToHashSet();
        Drain(population.Run());var selected=population.Roster.Guards.Single(value=>value.Refuge!=null);
        Need(!population.SurveyComplete && selected.Shift==GuardShift.Night,"one native ready site assigns a normal night guard before full survey completion");
        Need(population.Roster.Guards.Count==4 && ids.Contains(selected.Id) && selected.Refuge.StartsAt==72000 &&
            selected.Refuge.Exterior[0].X==selected.Post.X && selected.Refuge.Exterior.Last().X==8 &&
            selected.Refuge.WallRoute.Last().X==10 && selected.Refuge.Evaluate(72000).Phase!=RefugePhase.WallPosted,
            "assignment retains actual identity and measured route without inventing arrival or guards");
        Drain(population.Run());Need(population.Roster.Guards.Count(value=>value.Refuge!=null)==1,"already assigned actual site cannot receive another guard");
        population=new Population();population.geometry.Blocked=true;Drain(population.Run());
        Need(population.Roster.Guards.All(value=>value.Refuge==null),"incomplete native approach cannot fabricate a wall assignment");
        population=new Population();population.geometry.AfterRoute=()=>population.Valid=false;Drain(population.Run());
        Need(population.Roster.Guards.All(value=>value.Refuge==null),"cancelled route survey makes no late assignment");
        population=new Population();population.geometry.AfterRoute=()=>{foreach(var guard in population.Roster.Guards)guard.Post=new SavedRoutePoint {X=guard.Post.X+20};};Drain(population.Run());
        Need(population.Roster.Guards.All(value=>value.Refuge==null),"changed actual departure post invalidates surveyed route before mutation");
        population=new Population();population.Gates.Add(population.Roster.Guards[3].Id);Drain(population.Run());
        Need(population.Roster.Guards.All(value=>value.Refuge==null) && population.geometry.Queries==0,
            "existing gate exclusion and final available street guard reserve remain unchanged");
        Console.WriteLine(checks+" checks passed");return 0;
    }
}
