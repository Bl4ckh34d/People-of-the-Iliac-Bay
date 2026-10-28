using System;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.Tests;

static class Program
{
    static int assertions;
    static void Check(bool value,string name) { assertions++;if(!value)throw new Exception(name); }
    static int Main()
    {
        var store=new WorldStateStore(17);
        var town=store.GetOrCreate(7,new[]{13},3);
        var family=town.Households[0];
        Check(family.Residents.Count>=2,"fixture has family");
        family.Residents[0].PartnerId=family.Residents[1].Id;
        family.Residents[1].PartnerId=family.Residents[0].Id;
        var first=store.ReadNpcBiographyAnchor(family.Residents[0].Id);
        var second=store.ReadNpcBiographyAnchor(family.Residents[1].Id);
        Check(first!=null && first.FamilyId==second.FamilyId,"real family shares anchor");
        Check(store.ReadNpcBiographyAnchor("invented-person")==null,"unknown person cannot obtain biography");
        var unusableNativeReply=NpcBiographyChat.Parse("BIO|career/early learning|formative everyday experience|wish|NONE|NONE|NONE",first,4);
        Check(unusableNativeReply!=null && !store.TryCommitNpcBiography(first,unusableNativeReply) && store.ReadNpcBiography(first.PersonId)==null,
            "native placeholder response with duplicate family fields remains uncommitted");
        string facts="BIO|I learned my trade through patient practice.|A hard season taught me to save.|I hope to become better at my work.|We once mended a blanket together.|We sheltered indoors through a long storm.|We celebrated a small harvest meal.";
        var parsed=NpcBiographyChat.Parse(facts,first,5);
        Check(parsed!=null && parsed.FamilyFacts.Count==3,"bounded semantic biography parsed");
        Check(store.TryCommitNpcBiography(first,parsed),"first biography accepted");
        Check(!store.TryCommitNpcBiography(first,parsed),"existing biography immutable");
        var shared=store.ReadNpcFamilyBiography(second.FamilyId);
        Check(shared!=null && shared.Count==3,"family sees identical shared past");
        shared[0]="mutation";
        Check(store.ReadNpcFamilyBiography(second.FamilyId)[0]!="mutation","family read returns copy");
        var other=NpcBiographyChat.Parse("BIO|I learned by watching.|A quiet winter taught me patience.|I wish to improve.",second,6);
        Check(other!=null,"later family member needs only personal facts");
        Check(store.TryCommitNpcBiography(second,other),"later member joins fixed family history");
        Check(store.ReadNpcBiography(second.PersonId).FamilyFacts[0]==store.ReadNpcBiography(first.PersonId).FamilyFacts[0],"family facts remain exact across members");
        Check(NpcBiographyChat.Parse("NONE",first,7)==null,"irrelevant turn yields no biography");
        Check(NpcBiographyChat.Parse(facts+"|extra|extra",first,7)==null,"oversized family history rejected");
        Check(NpcBiographyChat.Parse("BIO|one|two|three|only one fact",first,7)==null,"partial family history rejected");
        var snapshot=store.Snapshot();
        var restored=new WorldStateStore(0);restored.Restore(snapshot);
        Check(restored.ReadNpcBiography(first.PersonId).CareerStart==parsed.CareerStart,"current save preserves personal biography");
        Check(restored.ReadNpcFamilyBiography(first.FamilyId)[2]==parsed.FamilyFacts[2],"current save preserves family past");
        Check(NpcBiographyChat.Prompt("What do you remember from childhood?","",first,null).Contains("NONE"),"same-model prompt supports irrelevant/no-generation result");
        Check(ConversationKnowledge.ParseRouting("map|-1|neutral|none").Intent=="map","structured route keeps model meaning");
        Check(ConversationKnowledge.ParseRouting("biography|-1|neutral|none").Intent=="biography","personal past uses semantic route");
        Check(NpcCompanionEnterpriseChat.Parse("PLAN|DungeonCrawl",null)?.PlanKind==NpcCompanionEnterpriseKind.DungeonCrawl,"planning binds exact enterprise kind");
        var enterprise=new NpcCompanionEnterprise {Id="enterprise:actual",Active=true};
        Check(NpcCompanionEnterpriseChat.Parse("CONTAINERS|enterprise:other",enterprise)==null,"permission cannot bind another enterprise");
        Check(NpcCompanionEnterpriseChat.Parse("OWN|enterprise:actual",enterprise)?.Operation=="OWN","own-kill permission binds current enterprise");
        Check(NpcCompanionEnterpriseChat.Parse("CONTAINERS|enterprise:actual",enterprise)?.Operation=="CONTAINERS","containers require separate explicit semantic decision");
        Check(NpcCompanionEnterpriseChat.Parse("PLAN|DragonHunt",enterprise)==null,"invented enterprise kind rejected");
        var task=new NpcTaskPlan {Id="task:independent",ProposalId="proposal:independent",ResidentId=first.PersonId,
            AcceptedAt=10,UpdatedAt=10,DeadlineAt=900};
        task.Steps.Add(new NpcTaskStep {Kind=NpcActionKind.MoveTo,Target=new NpcActionTarget {
            Kind=NpcActionTargetKind.WorldPoint,WorldSpaceId="settlement:7",SettlementId=7}});
        task.Steps.Add(new NpcTaskStep {Kind=NpcActionKind.MoveTo,Target=new NpcActionTarget {
            Kind=NpcActionTargetKind.WorldPoint,WorldSpaceId="settlement:7",SettlementId=7,X=2}});
        task.Steps.Add(new NpcTaskStep {Kind=NpcActionKind.Follow,DurationSeconds=30,ContinueAfterFailure=true,
            Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player"}});
        string reason;
        Check(ClassifiedAdmissionFixture.TryAcceptTask(store,task,true,out reason),"independent task accepted: "+reason);
        Check(store.FailNpcTask(task.Id,1,11,"No safe route reaches the first point."),"blocked task reconciles");
        var continued=store.ReadNpcTask(task.Id);
        Check(continued.Phase==NpcTaskPhase.Ready && continued.CurrentStepIndex==2,"explicit independent follow survives blocked dependent step");
        Check(continued.Steps[1].Blocked && continued.Steps[1].CompletedEffectId==null,"dependent step blocked without effect");
        var follow=new NpcActionJob {Id=NpcTaskContract.ActionId(continued),ProposalId=NpcTaskContract.ActionId(continued),ResidentId=first.PersonId,
            Kind=NpcActionKind.Follow,Target=NpcActionExecutor.CopyTarget(continued.Steps[2].Target),AcceptedAt=12,UpdatedAt=12,
            Phase=NpcActionPhase.Accepted,Revision=1,DeadlineAt=900,FinishAt=42};
        Check(store.TryDispatchNpcTaskStep(continued.Id,continued.Revision,12,follow,true,true,true,out reason),"independent follow dispatches: "+reason);
        Check(store.ObserveNpcAction(follow.Id,1,12,NpcActionObservation.Arrival()),"independent follow starts");
        Check(store.ObserveNpcAction(follow.Id,2,42,NpcActionObservation.Success("follow:actual-effect")),"independent follow completes");
        Check(store.ReconcileNpcTask(task.Id,3,42),"partial task completion reconciles");
        var partial=store.ReadNpcTask(task.Id);
        Check(partial.Phase==NpcTaskPhase.Failed && partial.CurrentStepIndex==3,"partial task never reports total success");
        Check(partial.FailureReceipt.Message.Contains("1 of 3") && partial.FailureReceipt.Message.Contains("not attempted"),"receipt reports actual completed and blocked effects");
        ConversationActionReply partialReply;
        Check(ConversationActionReply.TryFromSavedTask(store,task.Id,out partialReply) && partialReply.CompletedSteps==1 &&
            partialReply.Status==ConversationActionReplyStatus.Failed,"dialogue receipt never counts failed/skipped steps as completed");
        var resumed=new WorldStateStore(0);resumed.Restore(store.Snapshot());
        Check(resumed.ReadNpcTask(task.Id).Steps[1].Blocked,"blocked step persists across current save restore");
        var parserSteps=new[]{
            new NpcActionDecision {Option=new NpcActionOption {Kind=NpcActionKind.MoveTo}},
            new NpcActionDecision {Option=new NpcActionOption {Kind=NpcActionKind.Follow}},
            new NpcActionDecision {Option=new NpcActionOption {Kind=NpcActionKind.Transfer,ItemSourceStep=1}},
            new NpcActionDecision {Option=new NpcActionOption {Kind=NpcActionKind.Wait}}};
        string continuationGrammar=NpcTaskFailureChat.Grammar(parserSteps);
        Check(continuationGrammar.IndexOf("\"2\"",StringComparison.Ordinal)>=0 &&
            continuationGrammar.IndexOf("\"3\"",StringComparison.Ordinal)<0 &&
            continuationGrammar.IndexOf("\"4\"",StringComparison.Ordinal)<0,
            "continuation grammar offers Follow but never an acquired-item handover or dependent Wait");
        Check(NpcTaskFailureChat.Grammar(new[]{parserSteps[0],parserSteps[3]})=="root ::= \"STOP\"\n",
            "an arrival and dependent Wait offer only STOP");
        Check(NpcTaskFailureChat.Apply("CONTINUE|2",parserSteps) && parserSteps[1].ContinueAfterFailure,"semantic continuation selects existing independent step");
        Check(!NpcTaskFailureChat.Apply("CONTINUE|2|2",parserSteps),"duplicate continuation index rejected");
        Check(!NpcTaskFailureChat.Apply("CONTINUE|1",parserSteps),"first step cannot continue after earlier failure");
        Check(!NpcTaskFailureChat.Apply("CONTINUE|3",parserSteps),"acquired item handover remains dependent");
        Check(!NpcTaskFailureChat.Apply("CONTINUE|4",parserSteps),"arrival bound wait remains dependent");
        Check(NpcTaskFailureChat.Apply("STOP",parserSteps) && !parserSteps[1].ContinueAfterFailure,"default stop removes continuation metadata");
        Check(!NpcTaskFailureChat.Apply("CONTINUE|2",new NpcActionDecision[]{null,null}),"null decisions rejected without mutation");
        Check(!NpcTaskFailureChat.Apply("STOP",new NpcActionDecision[]{null,null}),"null default-stop decisions rejected without exception");
        var nativeStore=new WorldStateStore(42);nativeStore.GetOrCreate(1,new[]{8},1);
        var keeper=nativeStore.RegisterNativePerson(1,4,1,"Keeper Kinghart",ResidentSex.Male,1,41,182,1);
        var earlyAnchor=nativeStore.ReadNpcBiographyAnchor(keeper.Id);
        var early=NpcBiographyChat.Parse("BIO|I learned by helping with ordinary work.|I learned patience in a hard season.|I hope to improve my trade.",earlyAnchor,5);
        Check(nativeStore.TryCommitNpcBiography(earlyAnchor,early),"native biography can precede housing survey");
        Check(nativeStore.AssignNativeBed(keeper.Id,new HomeProfile(4,2),new HomeProfile[0]),"real later native bed assignment succeeds");
        var nativeSaved=nativeStore.Snapshot();
        Check(nativeSaved.NpcBiographies[0].CareerStart==early.CareerStart,"bed assignment does not invalidate existing biography save");
        Check(nativeStore.CreateKeeperHousehold(keeper.Id,new HomeProfile(4,2))!=null,"real later family creation preserves existing biography");
        var familyAnchor=nativeStore.ReadNpcBiographyAnchor(keeper.Id);
        Check(familyAnchor.FamilyId!=null,"later native family is grounded");
        var sharedOnly=NpcBiographyChat.Parse(facts,familyAnchor,6);
        Check(nativeStore.TryCommitNpcBiography(familyAnchor,sharedOnly),"first relevant family dialogue adds only real family's shared past");
        Check(nativeStore.ReadNpcBiography(keeper.Id).CareerStart==early.CareerStart,"family attachment never rewrites fixed personal facts");
        Check(nativeStore.ReadNpcFamilyBiography(familyAnchor.FamilyId).Count==3,"later real family receives shared facts");
        var nativeRestored=new WorldStateStore(0);nativeRestored.Restore(nativeStore.Snapshot());
        Check(nativeRestored.ReadNpcBiography(keeper.Id).CareerStart==early.CareerStart,"native family attachment survives save restore");
        int continuationCases=TaskContinuationChecks.Run();
        int existingCases=FocusedTaskChecks.Run();
        int paidTravelCases=PaidTravelTaskChecks.Run();
        Console.WriteLine("PASS "+assertions+" dialogue/biography assertions; "+continuationCases+" continuation cases; "+existingCases+" existing task/conversation cases; "+paidTravelCases+" paid travel cases");return 0;
    }
}
