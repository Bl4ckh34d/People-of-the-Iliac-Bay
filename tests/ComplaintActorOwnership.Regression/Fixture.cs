using System;
using PeopleOfTheIliacBay.Core;
public sealed class ResidentActor
{public string Id="actual-traveler";public static implicit operator bool(ResidentActor actor)=>actor!=null;}
public sealed class Plan {public Resident Resident=new Resident {Id="actual-traveler"};}
public sealed class Population
{
    public bool ComplaintOwns=true,CustodyOwns;
    public int ComplaintCalls,FlightCalls,OrdinaryCalls;
    public ResidentActor ObservedBody;public double ObservedAt;
    private bool ApplyNpcCustodyOverlay(Plan plan,ref ResidentActor actor,double now)=>CustodyOwns;
    private void InterruptResidentForFlight(string id,ResidentActor actor,double now){FlightCalls++;}
    private bool PlaceDialogueComplaint(ResidentActor actor,double now)
    {ComplaintCalls++;ObservedBody=actor;ObservedAt=now;return ComplaintOwns;}
    public bool Apply(Plan plan,ref ResidentActor actor,double now,AlarmPerson nativeReaction)
    {
        // ACTUAL_OWNER_DISPATCH
        OrdinaryCalls++;return false;
    }
}
public static class Fixture
{
    static int passed,failed;
    static void Need(bool value,string text){if(value){passed++;Console.WriteLine("PASS "+text);}else{failed++;Console.WriteLine("FAIL "+text);}}
    public static int Run()
    {
        var plan=new Plan();var body=new ResidentActor();var original=body;var population=new Population();
        Need(population.Apply(plan,ref body,123.25,null) && population.ComplaintCalls==1 && population.OrdinaryCalls==0,
            "existing traveler complaint owns movement before visit placement");
        Need(ReferenceEquals(body,original) && ReferenceEquals(population.ObservedBody,original) && population.ObservedAt==123.25,
            "same physical traveler body and observed time reach existing complaint handler");
        population=new Population();
        Need(!population.Apply(plan,ref body,124,new AlarmPerson {Reaction=CivilianReaction.Flee,HasPosition=true}) &&
            population.FlightCalls==1 && population.ComplaintCalls==0,"actual flight keeps priority over complaint movement");
        population=new Population();
        Need(!population.Apply(plan,ref body,124,new AlarmPerson {Reaction=CivilianReaction.Fight,HasPosition=true}) &&
            population.ComplaintCalls==0,"actual positioned native alarm is not replaced by complaint motion");
        population=new Population {ComplaintOwns=false};
        Need(!population.Apply(plan,ref body,124,null) && population.OrdinaryCalls==1 && ReferenceEquals(body,original),
            "body without complaint retains ordinary existing owner");
        population=new Population {CustodyOwns=true};
        Need(population.Apply(plan,ref body,124,null) && population.ComplaintCalls==0 && population.OrdinaryCalls==0,
            "existing custody ownership remains unchanged");
        Console.WriteLine(passed+" passed; "+failed+" failed");return failed==0?0:1;
    }
}
