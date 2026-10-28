using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Opt-in native acceptance. It reuses the ordinary conversation task,
    // physical door executor, and handoff clock; it creates no actor or room.
    internal static class NativeEnterWaitProbe
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private const int WaitSeconds=10;
        private const int EnterSeconds=900;

        private sealed class Fixture
        {
            internal ResidentActor Actor;
            internal NpcActionOption Enter,Wait;
            internal List<NpcActionDecision> Decisions;
            internal string BuildingName;
        }

        internal static IEnumerator Run(PeopleOfTheIliacBayMod host,StreetPopulation streets,string character)
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-enter-wait-probe")<0 ||
                string.IsNullOrWhiteSpace(character) || !NativeProbeText.IsTestCharacter(character))
                throw new InvalidOperationException("Enter/Wait probe requires its explicit flag and owned disposable character.");
            var game=GameManager.Instance;var store=Store(host);
            if(!host || !streets || store==null || !game || !streets.Ready || game.PlayerEntity==null ||
                game.PlayerEntity.Name!=character || game.PlayerEnterExit.IsPlayerInsideBuilding)
                throw new InvalidOperationException("Enter/Wait probe requires its owned character in a loaded exterior town.");
            var observerBefore=game.PlayerMotor.transform.position;
            string residentId=null,taskId=null;
            try
            {
                SetTimeOfDay(12*3600);
                yield return null;yield return null;
                Fixture fixture=null;string lastReason="Fixture selection did not run.";
                float fixtureDeadline=Time.realtimeSinceStartup+45f;
                while(Time.realtimeSinceStartup<fixtureDeadline && fixture==null)
                {
                    string fixtureReason;
                    fixture=FindFixture(streets,game,out fixtureReason);
                    lastReason=fixtureReason;
                    if(fixture==null){yield return new WaitForSecondsRealtime(0.25f);continue;}
                    string acceptReason;
                    if(!NpcProbeCommandSource.TryAcceptTask(streets,fixture.Actor.Resident.Id,
                        "native-enter-wait:"+Guid.NewGuid().ToString("N"),fixture.Decisions,out acceptReason))
                    {lastReason=acceptReason;fixture=null;yield return null;}
                }
                if(fixture==null)
                {
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ENTER_WAIT_FIXTURE_GATE: "+lastReason);
                    throw new InvalidOperationException("Enter/Wait fixture selection failed: "+lastReason);
                }
                residentId=fixture.Actor.Resident.Id;
                var identity=Identity(fixture.Actor.Resident);
                var inventory=store.ReadNpcInventory(residentId);
                var vitals=store.ReadNpcVitals(residentId,store.ResolveNpcArchetype(residentId));
                var accepted=store.ActiveNpcTask(residentId);
                if(accepted==null || accepted.Steps.Count!=2 || accepted.Steps[0].Kind!=NpcActionKind.Enter ||
                    accepted.Steps[1].Kind!=NpcActionKind.Wait ||
                    !NpcTaskPositionBinding.IsBoundWaitAfterStaticArrival(accepted.Steps[0],accepted.Steps[1]))
                    throw new InvalidOperationException("Native Enter/Wait task did not retain the exact public doorway.");
                taskId=accepted.Id;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ENTER_WAIT_ACCEPTED: resident="+residentId+"; task="+taskId+
                    "; door="+fixture.Enter.Target.DoorId+"; building="+fixture.Enter.Target.BuildingKey+
                    "; buildingType=Bank; buildingName="+fixture.BuildingName+".");
                var enter=store.ReadNpcAction(taskId+":step:0");
                if(enter==null || enter.Kind!=NpcActionKind.Enter || !NpcTaskContract.TargetMatches(enter.Target,fixture.Enter.Target))
                    throw new InvalidOperationException("Native Enter action was not dispatched with the selected exact doorway.");
                Vector3 door;float arrival;string resolveReason;
                if(!Resolve(streets,enter,out door,out arrival,out resolveReason))
                    throw new InvalidOperationException("Native Enter doorway could not be resolved: "+resolveReason);
                float nearestDoor=float.PositiveInfinity;NpcActionJob completedWait=null;NpcActionJob completedEnter=null;bool receiptLogged=false;
                float started=Time.realtimeSinceStartup,deadline=started+480f,nextProgress=started;
                Vector3 lastGround=fixture.Actor.transform.position-Vector3.up;
                float walked=0,maximumStep=0;
                while(Time.realtimeSinceStartup<deadline)
                {
                    if(game.PlayerEnterExit.IsPlayerInsideBuilding)
                        throw new InvalidOperationException("Enter/Wait probe player entered the building; offscreen evidence is invalid.");
                    var actor=FindActor(streets,residentId);
                    if(actor && actor.gameObject.activeInHierarchy)
                    {
                        var ground=actor.transform.position-Vector3.up;
                        float step=Vector3.Distance(lastGround,ground);walked+=step;maximumStep=Mathf.Max(maximumStep,step);lastGround=ground;
                        nearestDoor=Mathf.Min(nearestDoor,Vector3.Distance(actor.transform.position-Vector3.up,door));
                    }
                    var task=store.ReadNpcTask(taskId);
                    var observedEnter=store.ReadNpcAction(taskId+":step:0");
                    var observedWait=store.ReadNpcAction(taskId+":step:1");
                    bool failed=task==null || task.Phase==NpcTaskPhase.Failed || task.Phase==NpcTaskPhase.Cancelled;
                    if(Time.realtimeSinceStartup>=nextProgress || failed)
                    {
                        LogProgress(streets,game,actor,observedEnter,residentId,door,arrival,nearestDoor,walked,maximumStep,
                            Time.realtimeSinceStartup-started,failed?"failure":"progress");
                        nextProgress=Time.realtimeSinceStartup+5f;
                    }
                    if(observedEnter!=null && observedEnter.Phase==NpcActionPhase.Completed)
                    {
                        var receipt=observedEnter.BuildingTransitionReceipt;
                        if(receipt==null || receipt.DestinationObserved || receipt.DoorId!=fixture.Enter.Target.DoorId ||
                            receipt.SettlementId!=fixture.Enter.Target.SettlementId || receipt.BuildingKey!=fixture.Enter.Target.BuildingKey ||
                            receipt.SourceSpaceId!=fixture.Enter.Target.WorldSpaceId || receipt.DestinationSpaceId!=fixture.Enter.Target.DestinationSpaceId)
                            throw new InvalidOperationException("Enter receipt did not prove the exact unobserved native doorway handoff.");
                        completedEnter=observedEnter;
                        if(!receiptLogged)
                        {
                            receiptLogged=true;
                            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ENTER_WAIT_ENTERED: resident="+residentId+"; door="+
                                receipt.DoorId+"; observedAt="+receipt.ObservedGameTime.ToString("F3")+"; playerOutside=true.");
                        }
                    }
                    if(observedWait!=null && NpcActionExecutor.IsTerminal(observedWait))
                    {
                        if(observedWait.Phase!=NpcActionPhase.Completed)throw new InvalidOperationException("Offscreen building Wait did not complete normally.");
                        completedWait=observedWait;break;
                    }
                    if(failed)
                    {
                        string taskFailure=task==null?"missing":string.IsNullOrWhiteSpace(task.FailureReason)?"<none>":task.FailureReason;
                        string enterFailure=observedEnter==null?"missing":string.IsNullOrWhiteSpace(observedEnter.FailureReason)?"<none>":observedEnter.FailureReason;
                        string waitFailure=observedWait==null?"missing":string.IsNullOrWhiteSpace(observedWait.FailureReason)?"<none>":observedWait.FailureReason;
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ENTER_WAIT_FAILURE: resident="+residentId+"; task="+
                            (task==null?"missing":task.Phase.ToString())+"; taskFailure="+taskFailure+
                            "; enter="+(observedEnter==null?"missing":observedEnter.Phase.ToString())+"; enterFailure="+enterFailure+
                            "; wait="+(observedWait==null?"missing":observedWait.Phase.ToString())+"; waitFailure="+waitFailure+
                            "; actor="+(actor&&actor.gameObject.activeInHierarchy?"active":"absent")+
                            "; door="+fixture.Enter.Target.DoorId+"; building="+fixture.Enter.Target.BuildingKey+".");
                        throw new InvalidOperationException("Enter/Wait task failed before its offscreen timer completed: task="+
                            (task==null?"missing":task.Phase.ToString())+"; taskFailure="+taskFailure+
                            "; enter="+(observedEnter==null?"missing":observedEnter.Phase.ToString())+"; enterFailure="+enterFailure+
                            "; wait="+(observedWait==null?"missing":observedWait.Phase.ToString())+"; waitFailure="+waitFailure+".");
                    }
                    yield return null;
                }
                if(completedWait==null)
                    LogProgress(streets,game,FindActor(streets,residentId),store.ReadNpcAction(taskId+":step:0"),residentId,
                        door,arrival,nearestDoor,walked,maximumStep,Time.realtimeSinceStartup-started,"timeout");
                if(completedEnter==null || completedWait==null || nearestDoor>arrival+0.05f ||
                    completedWait.AcceptedAt+0.001d<completedEnter.BuildingTransitionReceipt.ObservedGameTime ||
                    completedWait.FinishAt-completedWait.AcceptedAt<WaitSeconds-0.001d ||
                    completedWait.UpdatedAt-completedWait.AcceptedAt<WaitSeconds-0.001d ||
                    !NpcTaskContract.TargetMatches(completedWait.Target,fixture.Enter.Target))
                    throw new InvalidOperationException("Enter/Wait physical-door or elapsed-duration evidence failed.");
                if(!SameIdentity(identity,FindResident(store.Snapshot(),residentId)) || !SameInventory(inventory,store.ReadNpcInventory(residentId)))
                    throw new InvalidOperationException("Enter/Wait changed the resident identity or inventory.");
                var afterVitals=store.ReadNpcVitals(residentId,store.ResolveNpcArchetype(residentId));
                if(!SameVitals(vitals,afterVitals))throw new InvalidOperationException("Enter/Wait changed the resident vitals.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ENTER_WAIT_NATIVE_PASS: resident="+residentId+"; door="+
                    fixture.Enter.Target.DoorId+"; nearestDoor="+nearestDoor.ToString("F3")+"; arrival="+arrival.ToString("F3")+
                    "; enteredAt="+completedEnter.BuildingTransitionReceipt.ObservedGameTime.ToString("F3")+"; waitElapsed="+
                    (completedWait.UpdatedAt-completedWait.AcceptedAt).ToString("F3")+
                    "; buildingType=Bank; building="+fixture.Enter.Target.BuildingKey+
                    "; playerOutside=true; inventoryUnchanged=true; identityUnchanged=true; vitalsUnchanged=true.");
                taskId=null;
            }
            finally
            {
                if(taskId!=null && residentId!=null){string ignored;streets.TryStopConversationAction(residentId,out ignored);}
                game.PlayerMotor.transform.position=observerBefore;Physics.SyncTransforms();
            }
        }

        // Read-only, opt-in diagnostics. Preserve the same player/body positions
        // and movement rules so the next run can distinguish fixture overlap,
        // waypoint blockage, missing support and native-navigation failure.
        private static void LogProgress(StreetPopulation streets,GameManager game,ResidentActor actor,NpcActionJob enter,
            string residentId,Vector3 acceptedDoor,float acceptedArrival,float nearestDoor,float walked,float maximumStep,
            float elapsed,string stage)
        {
            Vector3 target=acceptedDoor;float arrival=acceptedArrival;string reason="missing action";
            bool resolved=enter!=null && Resolve(streets,enter,out target,out arrival,out reason);
            var native=actor?actor.GetComponent<NativeResidentNavigation>():null;
            var actual=actor?actor.transform.position-Vector3.up:Vector3.zero;
            var player=game.PlayerMotor.transform.position;
            var delta=player-(actor?actor.transform.position:player);delta.y=0;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ENTER_WAIT_PROGRESS: stage="+stage+"; resident="+residentId+
                "; elapsedReal="+elapsed.ToString("F3",CultureInfo.InvariantCulture)+
                "; phase="+(enter==null?"missing":enter.Phase.ToString())+"; actor="+(actor?"active":"absent")+
                "; actualFoot="+actual.ToString("F3")+"; targetResolved="+resolved+"; target="+target.ToString("F3")+
                "; acceptedTarget="+acceptedDoor.ToString("F3")+"; gap="+(actor?Vector3.Distance(actual,target):float.PositiveInfinity).ToString("F3",CultureInfo.InvariantCulture)+
                "; nearestGap="+nearestDoor.ToString("F3",CultureInfo.InvariantCulture)+"; arrival="+arrival.ToString("F3",CultureInfo.InvariantCulture)+
                "; resolveReason="+(reason??"none")+"; walked="+walked.ToString("F3",CultureInfo.InvariantCulture)+
                "; maximumStep="+maximumStep.ToString("F3",CultureInfo.InvariantCulture)+
                "; player="+player.ToString("F3")+"; playerHorizontalGap="+delta.magnitude.ToString("F3",CultureInfo.InvariantCulture)+
                "; paused="+GameManager.IsGamePaused+"; conversing="+(actor && actor.IsConversing)+
                "; nativeActive="+(native && native.Active)+"; nativeTarget="+(native && native.HasDestination?native.Destination.ToString("F3"):"none")+
                "; nativeFailure="+(native?native.FailureReason??"none":"missing")+"; nativePath="+(native?native.PathState:"missing")+
                "; bodyOverlaps="+DescribeBodyOverlaps(actor,actual)+
                "; motion="+NativeMoveToWaitProbe.DescribeActionMotion(streets,residentId,actor));
        }

        private static string DescribeBodyOverlaps(ResidentActor actor,Vector3 ground)
        {
            if(!actor)return "actor-absent";
            var text=new StringBuilder();int count=0;
            foreach(var collider in Physics.OverlapCapsule(ground+Vector3.up*0.8f,ground+Vector3.up*1.6f,0.24f,~0,QueryTriggerInteraction.Ignore))
            {
                if(!collider || collider.transform.IsChildOf(actor.transform))continue;
                count++;
                if(count>8)continue;
                if(text.Length>0)text.Append(" | ");
                text.Append(collider.name).Append('<').Append(collider.GetType().Name).Append('>')
                    .Append(" player=").Append((bool)collider.GetComponentInParent<PlayerMotor>())
                    .Append(" resident=").Append((bool)collider.GetComponentInParent<ResidentActor>())
                    .Append(" bounds=").Append(collider.bounds.center.ToString("F3")).Append('/').Append(collider.bounds.size.ToString("F3"));
            }
            return count.ToString(CultureInfo.InvariantCulture)+" ["+text+"]";
        }

        private static Fixture FindFixture(StreetPopulation streets,GameManager game,out string reason)
        {
            var geometry=typeof(StreetPopulation).GetField("geometry",Private).GetValue(streets) as TownGeometry;
            var directory=typeof(StreetPopulation).GetField("directory",Private).GetValue(streets) as BuildingDirectory;
            int eligibleAdults=0,semanticWaits=0,semanticEnters=0,namedRequests=0,namedEnters=0,boundWaits=0,invalidPlans=0;
            foreach(var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
            {
                if(!actor || !actor.gameObject.activeInHierarchy || actor.Resident==null || !actor.Resident.IsAlive ||
                    actor.Resident.IsQuestProtected || actor.Resident.NativeIdentity!=null || actor.Resident.AgeGroup!=ResidentAgeGroup.Adult ||
                    actor.IsSleeping || actor.IsConversing || streets.IsAlarmed(actor.Resident.Id))continue;
                eligibleAdults++;
                Vector3 observerGround;
                if(geometry==null || !directory || !NativeMoveToWaitProbe.TryObserverGround(geometry,directory,
                    actor.transform.position-Vector3.up,out observerGround))continue;
                // Candidate lookup needs a nearby player, not an overlapping
                // player collider that prevents the accepted actor from moving.
                game.PlayerMotor.transform.position=observerGround+Vector3.up;Physics.SyncTransforms();
                var semanticOptions=streets.ConversationActionCandidates(actor.Resident.Id);NpcActionOption enter=null;
                foreach(var option in semanticOptions)
                {
                    if(option!=null && option.Kind==NpcActionKind.Wait && option.Target!=null)semanticWaits++;
                    if(option!=null && option.Kind==NpcActionKind.Enter && option.Target!=null &&
                        option.Target.Kind==NpcActionTargetKind.Building &&
                        IsBank(directory,option.Target.BuildingKey,actor.Resident.HomeKey))
                        {enter=option;semanticEnters++;}
                }
                // This controlled adapter probe selects an exact native bank,
                // including banks outside the bounded semantic catalogue. It
                // does not infer intent from the building name or bypass task
                // admission, the unlocked-door check, or physical movement.
                if(enter==null && geometry!=null && directory)
                    foreach(var pair in geometry.Entrances)
                    {
                        if(!IsBank(directory,pair.Key,actor.Resident.HomeKey))continue;
                        string name=game.TalkManager.GetBuildingNameForBuildingKey(pair.Key);
                        if(string.IsNullOrWhiteSpace(name))continue;
                        namedRequests++;
                        var named=new List<NpcActionOption>();
                        var offer=typeof(StreetPopulation).GetMethod("OfferBuildingEntry",Private);
                        offer.Invoke(streets,new object[] {actor,named,pair.Key,name,null,true});
                        foreach(var option in named)
                            if(option!=null && option.Kind==NpcActionKind.Enter && option.Target!=null &&
                                option.Target.Kind==NpcActionTargetKind.Building && option.Target.BuildingKey==pair.Key)
                            {enter=option;namedEnters++;break;}
                        if(enter!=null)break;
                    }
                if(enter==null)continue;
                int semanticMaximum=0;foreach(var option in semanticOptions)if(option!=null && option.Id>semanticMaximum)semanticMaximum=option.Id;
                int uniqueMaximum=Math.Max(semanticMaximum,enter.Id);
                var waits=NpcActionSequenceChat.BindWaitOptionsAfterMoveTo(new NpcActionDecision {Option=enter,DurationSeconds=EnterSeconds},semanticOptions);
                NpcActionOption wait=null;
                foreach(var option in waits)
                    if(option!=null && option.Kind==NpcActionKind.Wait && option.Id>semanticMaximum &&
                        NpcTaskContract.TargetMatches(option.Target,enter.Target))
                    {
                        boundWaits++;
                        wait=Copy(option);
                        if(wait.Id<=uniqueMaximum)
                        {
                            if(uniqueMaximum==int.MaxValue)continue;
                            wait.Id=++uniqueMaximum;
                        }
                        break;
                    }
                if(wait==null)continue;
                var plan=new NpcTaskPlan {Id="native-enter-wait-probe",ProposalId="native-enter-wait-probe",ResidentId=actor.Resident.Id,
                    AcceptedAt=0,UpdatedAt=0,DeadlineAt=86400};
                plan.Steps.Add(new NpcTaskStep {Kind=NpcActionKind.Enter,Target=NpcActionExecutor.CopyTarget(enter.Target)});
                plan.Steps.Add(new NpcTaskStep {Kind=NpcActionKind.Wait,Target=NpcActionExecutor.CopyTarget(wait.Target),DurationSeconds=WaitSeconds});
                try {NpcTaskContract.Validate(plan);}catch(ArgumentException){invalidPlans++;continue;}
                reason="Fixture selected with exact Enter and semantic Wait source.";
                return new Fixture {Actor=actor,Enter=enter,Wait=wait,
                    BuildingName=game.TalkManager.GetBuildingNameForBuildingKey(enter.Target.BuildingKey),Decisions=new List<NpcActionDecision> {
                    new NpcActionDecision {Option=Copy(enter),DurationSeconds=EnterSeconds},new NpcActionDecision {Option=Copy(wait),DurationSeconds=WaitSeconds}}};
            }
            if(eligibleAdults==0)reason="No active ordinary adult is available at noon.";
            else if(semanticWaits==0)reason="Active ordinary adults offered no current native Wait option at noon.";
            else if(semanticEnters==0 && namedRequests==0)reason="No surveyed non-home native Bank was available; an Inn or other building cannot substitute.";
            else if(semanticEnters==0 && namedEnters==0)reason="Native Bank requests found no currently unlocked exact Enter option; adults="+eligibleAdults+"; semanticWaits="+semanticWaits+"; namedRequests="+namedRequests+".";
            else if(boundWaits==0)reason="Exact Enter options existed, but semantic Wait options produced no exact bound doorway Wait; semanticEnters="+semanticEnters+"; namedEnters="+namedEnters+"; semanticWaits="+semanticWaits+".";
            else reason="Exact Enter/Wait task plan was rejected; boundWaits="+boundWaits+"; invalidPlans="+invalidPlans+".";
            return null;
        }

        private static bool IsBank(BuildingDirectory directory,int buildingKey,int homeKey)
        {
            BuildingSummary summary;
            return directory && buildingKey!=homeKey && directory.GetBuildingSummary(buildingKey,out summary) &&
                summary.BuildingType==DFLocation.BuildingTypes.Bank;
        }

        private static void SetTimeOfDay(double seconds)
        {
            var clock=DaggerfallUnity.Instance.WorldTime.Now;
            clock.RaiseTime((float)((seconds-clock.ToSeconds()%86400d+86400d)%86400d));
        }

        private static NpcActionOption Copy(NpcActionOption source)
        {return new NpcActionOption {Id=source.Id,Kind=source.Kind,Label=source.Label,ItemSourceStep=source.ItemSourceStep,
            MaximumQuantity=source.MaximumQuantity,Target=NpcActionExecutor.CopyTarget(source.Target)};}
        private static WorldStateStore Store(PeopleOfTheIliacBayMod host)
        {var field=typeof(PeopleOfTheIliacBayMod).GetField("store",Private);return field==null?null:field.GetValue(host) as WorldStateStore;}
        private static ResidentActor FindActor(StreetPopulation streets,string id)
        {var field=typeof(StreetPopulation).GetField("actors",Private);var actors=field==null?null:field.GetValue(streets) as Dictionary<string,ResidentActor>;ResidentActor actor;return actors!=null && actors.TryGetValue(id,out actor)?actor:null;}
        private static bool Resolve(StreetPopulation streets,NpcActionJob job,out Vector3 target,out float arrival,out string reason)
        {
            target=Vector3.zero;arrival=0;reason="resolver unavailable";var method=typeof(StreetPopulation).GetMethod("ResolveActionTarget",Private);
            if(method==null)return false;object[] args={job,Vector3.zero,0f,null};
            try {if(!(bool)method.Invoke(streets,args)){reason=args[3] as string;return false;}target=(Vector3)args[1];arrival=(float)args[2];reason=null;return true;}
            catch(Exception error){reason=error.GetBaseException().Message;return false;}
        }
        private static Resident FindResident(WorldState state,string id)
        {
            foreach(var settlement in state.Settlements)foreach(var household in settlement.Households)
                foreach(var resident in household.Residents)if(resident.Id==id)return resident;
            return null;
        }
        private static Resident Identity(Resident source)
        {return source==null?null:new Resident {Id=source.Id,FullName=source.FullName,Race=source.Race,BedSlot=source.BedSlot,
            HomeKey=source.HomeKey,AppearanceSeed=source.AppearanceSeed,IsAlive=source.IsAlive,IsQuestProtected=source.IsQuestProtected,
            AgeGroup=source.AgeGroup,Sex=source.Sex,PartnerId=source.PartnerId,MotherId=source.MotherId,FatherId=source.FatherId};}
        private static bool SameIdentity(Resident a,Resident b)
        {return a!=null && b!=null && a.Id==b.Id && a.FullName==b.FullName && a.Race==b.Race && a.BedSlot==b.BedSlot &&
            a.HomeKey==b.HomeKey && a.AppearanceSeed==b.AppearanceSeed && a.IsAlive==b.IsAlive && a.IsQuestProtected==b.IsQuestProtected &&
            a.AgeGroup==b.AgeGroup && a.Sex==b.Sex && a.PartnerId==b.PartnerId && a.MotherId==b.MotherId && a.FatherId==b.FatherId;}
        private static bool SameInventory(NpcInventoryState a,NpcInventoryState b)
        {
            if(a==null || b==null)return a==b;
            if(a.PersonId!=b.PersonId || a.WalletGold!=b.WalletGold || a.Revision!=b.Revision || a.LastTransactionId!=b.LastTransactionId || a.Items.Count!=b.Items.Count)return false;
            for(int i=0;i<a.Items.Count;i++)if(a.Items[i].Uid!=b.Items[i].Uid || a.Items[i].NativePayload!=b.Items[i].NativePayload)return false;
            return true;
        }
        private static bool SameVitals(NpcVitalsView a,NpcVitalsView b)
        {return a!=null && b!=null && a.IsAlive==b.IsAlive && a.IsPersisted==b.IsPersisted && a.MaximumHealth==b.MaximumHealth &&
            a.CurrentHealth==b.CurrentHealth && a.MaximumMagicka==b.MaximumMagicka && a.CurrentMagicka==b.CurrentMagicka && a.Revision==b.Revision;}
    }
}
