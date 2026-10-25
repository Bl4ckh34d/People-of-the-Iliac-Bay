using System;
using PeopleOfTheIliacBay.Core;

internal static class Program
{
    private static int Main()
    {
        int failed=0;
        Check("Observed recovery outdoors never becomes ordinary travel",()=>
        {
            var actual=ConversationLifeContext.ObservedActivity(new ActivitySnapshot((Activity)6,-1,new RoutePoint(2,3),0),true,-1);
            Require((int)actual.Activity==6);
        },ref failed);
        Check("Observed recovery indoors never becomes a social visit",()=>
        {
            var actual=ConversationLifeContext.ObservedActivity(new ActivitySnapshot((Activity)6,42,new RoutePoint(2,3),0),false,42);
            Require((int)actual.Activity==6);
        },ref failed);
        Check("Recovery context does not promise scheduled work or claim healing",()=>
        {
            var text=ConversationLifeContext.RoutineStatus(new ResidentRoutine(),12345,
                new ActivitySnapshot((Activity)6,42,new RoutePoint(2,3),0),0,key=>"Known house");
            Require(text.Contains("recovery") && text.Contains("interrupted") && !text.Contains("fully healed"));
        },ref failed);
        Check("Healing feedback requires a confirmed improvement receipt",()=>
        {
            var job=new NpcActionJob {Kind=NpcActionKind.UseHealingItem,Phase=NpcActionPhase.Completed,CompletedEffectId="effect"};
            Require(NpcActionFeedback.Describe(job,"Ada",null,null)==null);
            job.TreatmentReceipt=(NpcTreatmentReceipt)Activator.CreateInstance(typeof(NpcTreatmentReceipt),true);
            job.TreatmentReceipt.Kind=NpcTreatmentKind.HealingItem;
            job.TreatmentReceipt.HealthBefore=3;job.TreatmentReceipt.HealthAfter=8;
            var text=NpcActionFeedback.Describe(job,"Ada",null,null);
            Require(text!=null && text.Contains("healing potion") && !text.Contains("fully healed"));
        },ref failed);
        Console.WriteLine((4-failed)+"/4 passed");return failed==0?0:1;
    }
    private static void Require(bool value){if(!value)throw new Exception("Recovery activity was not preserved truthfully.");}
    private static void Check(string name,Action action,ref int failed)
    {try{action();Console.WriteLine("PASS "+name);}catch(Exception error){failed++;Console.WriteLine("FAIL "+name+": "+error.Message);}}
}
