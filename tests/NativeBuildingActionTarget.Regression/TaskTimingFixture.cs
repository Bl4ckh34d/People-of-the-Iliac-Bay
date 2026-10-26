using System;
using System.Collections.Generic;
using System.Reflection;
using PeopleOfTheIliacBay.Core;

namespace TaskTimingFixture
{
    public struct Vector3
    {
        public float x,y,z;
        public static Vector3 up => new Vector3 {y=1};
        public static Vector3 operator -(Vector3 a,Vector3 b)=>new Vector3 {x=a.x-b.x,y=a.y-b.y,z=a.z-b.z};
        public static float Distance(Vector3 a,Vector3 b)=>(float)Math.Sqrt((a.x-b.x)*(a.x-b.x)+(a.y-b.y)*(a.y-b.y)+(a.z-b.z)*(a.z-b.z));
    }
    public sealed class Transform {public Vector3 position;}
    public sealed class GameObject {public bool activeInHierarchy=true;}
    public sealed class ResidentActor
    {public Transform transform=new Transform();public GameObject gameObject=new GameObject();public static bool operator !(ResidentActor value)=>value==null;}
    public sealed class GameManager
    {public static GameManager Instance=new GameManager();public ResidentActor PlayerMotor=new ResidentActor();}
    public static class NativeNpcTempleTraining
    {public const string TargetContainerId="native:temple-training";public const int DurationSeconds=10800;}
    public static class NativeNpcShopPurchaseProbe
    {public static bool ShouldInjectTaskFailure(NpcTaskPlan task,NpcActionJob prior,out string reason){reason=null;return false;}}

    public sealed class Fixture
    {
        private bool Ready=true;
        private WorldStateStore worldStore=new WorldStateStore(900u);
        private ResidentActor actor=new ResidentActor();
        private string residentId;
        private double now=100;
        private NpcActionDecision dispatched;
        private NpcConversationCommandTurn turn;
        private static int failures;
        private Fixture()
        {
            residentId=worldStore.GetOrCreate(7,new[] {10},1).Households[0].Residents[0].Id;
            NpcConversationCommandSession session;
            Require(worldStore.TryBeginNpcConversationCommandSession(residentId,"timing",out session),"session");
            var binding=new ConversationContextBinding(ConversationActorCategory.Mobile,residentId,null,"timing",1,1,
                worldStore.NpcFrameworkScopeId,"","scene:test",1);
            Require(worldStore.TryCaptureNpcConversationCommandTurn(session,binding,"Enter the bank, then wait.",out turn),"turn");
        }
        private ResidentActor FindActor(string id)=>id==residentId?actor:null;
        private double ActionNow()=>now;
        private bool ActionSpaceMatches(NpcActionTarget target)=>true;
        private NpcActionTarget ActionPosition(Vector3 point)=>new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,SettlementId=7,BuildingKey=88};
        private bool ResolveActionTarget(NpcActionJob job,out Vector3 target,out float range,out string reason)
        {target=actor.transform.position-Vector3.up;range=.4f;reason=null;return true;}
        // Controlled native action boundary; the real Core admission, deadline and
        // snapshot rules still execute. No movement or appointment success claimed.
        private bool TryAcceptConversationAction(string resident,string proposal,NpcActionDecision decision,NpcTaskPlan task,
            NpcConversationCommandTurn ignored,out string reason)
        {
            dispatched=decision;
            bool timed=decision.Option.Kind==NpcActionKind.Wait || decision.Option.Kind==NpcActionKind.Follow || decision.Option.Kind==NpcActionKind.TakeSeat;
            var action=new NpcActionJob {Id=proposal,ProposalId=proposal,ResidentId=resident,Kind=decision.Option.Kind,
                Target=NpcActionExecutor.CopyTarget(decision.Option.Target),Phase=NpcActionPhase.Accepted,Revision=1,
                AcceptedAt=now,UpdatedAt=now,DeadlineAt=Math.Min(now+decision.DurationSeconds+(timed?60:0),task.DeadlineAt),
                FinishAt=timed?now+decision.DurationSeconds:0};
            return worldStore.TryDispatchNpcTaskStep(task.Id,task.Revision,now,action,true,true,true,out reason);
        }

        // TryAcceptConversationTaskCore
        // DispatchConversationTaskStep

        private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        private static FieldInfo BudgetField=>typeof(NpcTaskStep).GetField("ExecutionBudgetSeconds");
        private static int Budget(NpcTaskStep step)=>BudgetField==null?-1:(int)BudgetField.GetValue(step);
        private static void SetBudget(NpcTaskStep step,int value)
        {Require(BudgetField!=null,"execution budget field missing");BudgetField.SetValue(step,value);}
        private List<NpcActionDecision> Decisions(int budget)
        {
            var door=new NpcActionTarget {Kind=NpcActionTargetKind.Building,SettlementId=7,BuildingKey=88,
                DoorId="door:7:88:1:2:3",WorldSpaceId="settlement:7",DestinationSpaceId="interior:7:88"};
            return new List<NpcActionDecision> {
                new NpcActionDecision {DurationSeconds=budget,Option=new NpcActionOption {Kind=NpcActionKind.Enter,Target=door}},
                new NpcActionDecision {DurationSeconds=30,Option=new NpcActionOption {Kind=NpcActionKind.Wait,Target=NpcActionExecutor.CopyTarget(door)}}};
        }
        public static int Run()
        {
            Run("actual runtime builder and dispatch preserve900 separately from Wait30",()=>{
                var f=new Fixture();string reason;
                Require(f.TryAcceptConversationTaskCore(f.residentId,"timing",f.Decisions(900),f.turn,false,out reason),reason);
                Require(f.dispatched.DurationSeconds==900,"runtime dispatch replaced Enter900 with "+f.dispatched.DurationSeconds);
                var task=f.worldStore.ActiveNpcTask(f.residentId);var enter=f.worldStore.ReadNpcAction(task.ActiveActionId);
                Require(task.DeadlineAt==1150 && enter.DeadlineAt==1000 && enter.FinishAt==0,"task envelope/untimed Enter deadline");
                Require(Budget(task.Steps[0])==900 && task.Steps[0].DurationSeconds==0 && Budget(task.Steps[1])==0 && task.Steps[1].DurationSeconds==30,"budget distinct from dwell");
                var restored=new WorldStateStore(0);restored.Restore(f.worldStore.Snapshot());f.worldStore=restored;
                task=restored.ActiveNpcTask(f.residentId);Require(Budget(task.Steps[0])==900,"restore lost budget");
                Require(restored.ObserveNpcAction(enter.Id,enter.Revision,600,NpcActionObservation.Arrival()),"late physical arrival within900");
                enter=restored.ReadNpcAction(enter.Id);
                var receipt=NpcBuildingTransitionReceipt.Create(enter,f.residentId,7,88,enter.Target.DoorId,"settlement:7","interior:7:88",601);
                Require(restored.TryCompleteNpcBuildingTransition(enter.Id,enter.Revision,601,receipt),"late crossing");
                enter=restored.ReadNpcAction(enter.Id);
                Require(restored.SaveNpcActionPosition(enter.Id,enter.Revision,f.ActionPosition(new Vector3()),true,601),"observed landing");
                Require(restored.ReconcileNpcTask(task.Id,task.Revision,601),"reconcile");
                task=restored.ReadNpcTask(task.Id);f.now=610;
                Require(f.DispatchConversationTaskStep(task,out reason),reason);
                Require(f.dispatched.DurationSeconds==30,"Wait duration changed");
                var wait=restored.ReadNpcAction(NpcTaskContract.ActionId(task));
                Require(wait.FinishAt==640 && wait.DeadlineAt==700,"Wait starts at dispatch with existing grace");
            });
            Run("runtime rejects out-of-bound explicit travel budget",()=>{
                foreach(int budget in new[] {9,901}) {var f=new Fixture();string reason;
                    Require(!f.TryAcceptConversationTaskCore(f.residentId,"invalid",f.Decisions(budget),f.turn,false,out reason),"accepted invalid budget "+budget);}
            });
            Run("capability binds budget and store rejects changed action deadline",()=>{
                var f=new Fixture();var decisions=f.Decisions(900);
                var task=new NpcTaskPlan {Id="candidate",ProposalId="candidate",ResidentId=f.residentId,AcceptedAt=100,UpdatedAt=100,DeadlineAt=1150};
                task.Steps.Add(new NpcTaskStep {Kind=NpcActionKind.Enter,Target=decisions[0].Option.Target});SetBudget(task.Steps[0],900);
                task.Steps.Add(new NpcTaskStep {Kind=NpcActionKind.Wait,Target=decisions[1].Option.Target,DurationSeconds=30});
                NpcTaskPlan prepared;NpcConversationCommandCapability capability;string reason;
                Require(f.worldStore.TryPrepareNpcConversationTask(f.turn,task,out prepared),"prepare");
                Require(f.worldStore.TryIssueNpcConversationTask(f.turn,prepared,out capability),"issue");
                SetBudget(prepared.Steps[0],899);
                Require(!f.worldStore.TryAcceptNpcTaskFromConversation(capability,prepared,true,out reason),"changed budget reused capability");
                SetBudget(prepared.Steps[0],900);
                Require(f.worldStore.TryAcceptNpcTaskFromConversation(capability,prepared,true,out reason),reason);
                var wrong=new NpcActionJob {Id=NpcTaskContract.ActionId(prepared),ProposalId=NpcTaskContract.ActionId(prepared),
                    ResidentId=f.residentId,Kind=NpcActionKind.Enter,Target=NpcActionExecutor.CopyTarget(prepared.Steps[0].Target),
                    Phase=NpcActionPhase.Accepted,Revision=1,AcceptedAt=100,UpdatedAt=100,DeadlineAt=400};
                Require(!f.worldStore.TryDispatchNpcTaskStep(prepared.Id,prepared.Revision,100,wrong,true,true,true,out reason),
                    "store admitted a deadline shorter than the accepted900 budget");
                f.now=100;Require(f.DispatchConversationTaskStep(prepared,out reason),reason);
                var snapshot=f.worldStore.Snapshot();snapshot.NpcActions[0].DeadlineAt=1001;
                bool rejected=false;try {new WorldStateStore(0).Restore(snapshot);}catch(ArgumentException){rejected=true;}
                Require(rejected,"restored action deadline escaped accepted budget");
            });
            Run("zero budget retains default and timed/service steps cannot acquire travel budget",()=>{
                var f=new Fixture();var decisions=f.Decisions(300);string reason;
                var task=new NpcTaskPlan {Id="default",ProposalId="default",ResidentId=f.residentId,AcceptedAt=100,UpdatedAt=100,DeadlineAt=550};
                task.Steps.Add(new NpcTaskStep {Kind=NpcActionKind.Enter,Target=decisions[0].Option.Target});
                task.Steps.Add(new NpcTaskStep {Kind=NpcActionKind.Wait,Target=decisions[1].Option.Target,DurationSeconds=30});
                NpcTaskPlan prepared;NpcConversationCommandCapability capability;
                Require(f.worldStore.TryPrepareNpcConversationTask(f.turn,task,out prepared),"prepare default");
                Require(f.worldStore.TryIssueNpcConversationTask(f.turn,prepared,out capability),"issue default");
                Require(f.worldStore.TryAcceptNpcTaskFromConversation(capability,prepared,true,out reason),reason);
                Require(f.DispatchConversationTaskStep(prepared,out reason),reason);
                Require(f.dispatched.DurationSeconds==300,"zero budget default changed");
                SetBudget(task.Steps[1],10);
                bool rejected=false;try {NpcTaskContract.Validate(task);}catch(ArgumentException){rejected=true;}
                Require(rejected,"Wait acquired execution budget");SetBudget(task.Steps[1],0);
                task.DeadlineAt=task.AcceptedAt+86401;
                rejected=false;try {NpcTaskContract.Validate(task);}catch(ArgumentException){rejected=true;}
                Require(rejected,"task cap changed");
                foreach(var kind in new[] {NpcActionKind.Wait,NpcActionKind.Follow,NpcActionKind.TakeSeat,NpcActionKind.RestToHeal,
                    NpcActionKind.Purchase,NpcActionKind.Transfer,NpcActionKind.UseHealingItem,NpcActionKind.CastSpell})
                    Require(!NpcTaskContract.SupportsExecutionBudget(kind),"service/timed behavior changed for "+kind);
                foreach(var kind in new[] {NpcActionKind.MoveTo,NpcActionKind.Enter,NpcActionKind.Exit,NpcActionKind.Lead,NpcActionKind.Interact,NpcActionKind.ReportMurder})
                    Require(NpcTaskContract.Supported(kind) && NpcTaskContract.SupportsExecutionBudget(kind),"invalid movement budget kind "+kind);
            });
            return failures;
        }
        private static void Run(string name,Action test)
        {try {test();Console.WriteLine("PASS "+name);}catch(Exception error){failures++;Console.WriteLine("FAIL "+name+": "+error.Message);}}
    }
}
