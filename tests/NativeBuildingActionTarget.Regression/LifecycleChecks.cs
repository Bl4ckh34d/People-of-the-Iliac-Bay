using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.Tests;

internal static class LifecycleChecks
{
    private static void Require(bool condition,string description)
    {if(!condition)throw new InvalidOperationException(description);}

    internal static int Run()
    {
        int failures=0;
        foreach(string scenario in new[] {"elapsed", "cancel-unobserved", "cancel-observed"})
        {
            try {RunScenario(scenario);Console.WriteLine("PASS building Wait lifecycle: "+scenario);}
            catch(Exception error){failures++;Console.WriteLine("FAIL "+scenario+": "+error.Message);}
        }
        return failures;
    }

    private static void RunScenario(string scenario)
    {
        var store=new WorldStateStore(723u);
        var resident=store.GetOrCreate(7,new[] {10},1).Households[0].Residents[0];
        var doorway=new NpcActionTarget {Kind=NpcActionTargetKind.Building,SettlementId=7,BuildingKey=88,
            DoorId="door:7:88:1:2:3",WorldSpaceId="world:7",DestinationSpaceId="interior:7:88"};
        var task=new NpcTaskPlan {Id="bank:"+scenario,ProposalId="bank:"+scenario,ResidentId=resident.Id,
            AcceptedAt=100,UpdatedAt=100,DeadlineAt=500,Revision=1,Phase=NpcTaskPhase.Ready,
            Steps=new List<NpcTaskStep> {
                new NpcTaskStep {Kind=NpcActionKind.Enter,Target=NpcActionExecutor.CopyTarget(doorway)},
                new NpcTaskStep {Kind=NpcActionKind.Wait,DurationSeconds=30,Target=NpcActionExecutor.CopyTarget(doorway)}}};
        string reason;
        Require(ClassifiedAdmissionFixture.TryAcceptTask(store,task,true,out reason),reason);
        var enter=new NpcActionJob {Id=NpcTaskContract.ActionId(task),ProposalId=NpcTaskContract.ActionId(task),
            ResidentId=resident.Id,Kind=NpcActionKind.Enter,Target=NpcActionExecutor.CopyTarget(doorway),
            AcceptedAt=100,UpdatedAt=100,DeadlineAt=500,Revision=1,Phase=NpcActionPhase.Accepted};
        Require(store.TryDispatchNpcTaskStep(task.Id,task.Revision,100,enter,true,true,true,out reason),reason);
        Require(store.ObserveNpcAction(enter.Id,enter.Revision,105,NpcActionObservation.Arrival()),"physical door arrival");
        enter=store.ReadNpcAction(enter.Id);
        var receipt=NpcBuildingTransitionReceipt.Create(enter,resident.Id,7,88,doorway.DoorId,
            doorway.WorldSpaceId,doorway.DestinationSpaceId,106);
        Require(store.TryCompleteNpcBuildingTransition(enter.Id,enter.Revision,106,receipt),"exact native crossing");
        enter=store.ReadNpcAction(enter.Id);
        var outside=new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,SettlementId=7,WorldSpaceId="world:7",X=20,Z=30};
        Require(store.SaveNpcActionPosition(enter.Id,enter.Revision,outside,true,106),"retain observed source and recovery owner");
        task=store.ReadNpcTask(task.Id);
        Require(store.ReconcileNpcTask(task.Id,task.Revision,106),"advance to bound Wait");
        task=store.ReadNpcTask(task.Id);
        if(scenario=="elapsed")
        {
            Require(!store.TryCompleteOffscreenBuildingWait(task.Id,task.Revision,135),"never finish before crossing plus duration");
            Require(store.TryCompleteOffscreenBuildingWait(task.Id,task.Revision,136),"finish at crossing plus duration");
            var wait=store.ReadNpcAction(task.Id+":step:1");
            Require(wait.AcceptedAt==106 && wait.FinishAt==136 && NpcTaskContract.TargetMatches(wait.Target,doorway),"wait keeps time and bank identity");
            Require(!store.ReadNpcAction(enter.Id).BuildingTransitionReceipt.DestinationObserved,"offscreen wait invents no room position");
        }
        else if(scenario=="cancel-unobserved")
        {
            Require(store.CancelNpcTask(task.Id,task.Revision,110,"Stopped by player."),"cancel pending building Wait");
            task=store.ReadNpcTask(task.Id);
            Require(!store.TryCompleteOffscreenBuildingWait(task.Id,task.Revision,140),"cancel prevents later wait effect");
            Require(store.ReadNpcAction(task.Id+":step:1")==null,"cancel creates no fabricated Wait");
            Require(store.ReadNpcAction(enter.Id).ReturningToRoutine,"completed crossing keeps existing recovery owner");
        }
        else
        {
            enter=store.ReadNpcAction(enter.Id);
            var inside=new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,SettlementId=7,BuildingKey=88,X=2,Y=3,Z=4};
            Require(store.SaveNpcActionPosition(enter.Id,enter.Revision,inside,true,110),"observed exact bank landing");
            enter=store.ReadNpcAction(enter.Id);
            Require(NpcTaskPositionBinding.HasCompletedStaticArrivalReceipt(task.Steps[0],enter,task.Steps[1]),"observed landing enables bound Wait");
            var wait=new NpcActionJob {Id=NpcTaskContract.ActionId(task),ProposalId=NpcTaskContract.ActionId(task),
                ResidentId=resident.Id,Kind=NpcActionKind.Wait,Target=NpcActionExecutor.CopyTarget(doorway),
                AcceptedAt=110,UpdatedAt=110,DeadlineAt=500,FinishAt=140,Revision=1,Phase=NpcActionPhase.Accepted};
            Require(store.TryDispatchNpcTaskStep(task.Id,task.Revision,110,wait,true,true,true,out reason),reason);
            Require(store.ObserveNpcAction(wait.Id,wait.Revision,110,NpcActionObservation.Arrival()),"observed Wait starts at native landing");
            wait=store.ReadNpcAction(wait.Id);
            Require(store.SaveNpcActionPosition(wait.Id,wait.Revision,inside,false,111),"retain correct observed waiting position");
            task=store.ReadNpcTask(task.Id);
            Require(store.CancelNpcTask(task.Id,task.Revision,115,"Stopped by player."),"cancel observed Wait");
            wait=store.ReadNpcAction(wait.Id);
            Require(store.SaveNpcActionPosition(wait.Id,wait.Revision,inside,true,115),"hand cancelled Wait to native recovery");
            wait=store.ReadNpcAction(wait.Id);
            Require(wait.Phase==NpcActionPhase.Cancelled && wait.ReturningToRoutine && wait.LastPosition.BuildingKey==88,
                "Stop preserves bank position for native routine return");
        }
        var restored=new WorldStateStore(1);restored.Restore(store.Snapshot());
        Require(restored.ReadNpcAction(enter.Id).BuildingTransitionReceipt.DoorId==doorway.DoorId,"restore preserves exact crossing");
        Require(restored.ActiveNpcTask(resident.Id)==null,"completed or cancelled task relinquishes ownership");
    }
}
