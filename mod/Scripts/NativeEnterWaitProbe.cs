using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
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

        private sealed class Fixture
        {
            internal ResidentActor Actor;
            internal NpcActionOption Enter,Wait;
            internal List<NpcActionDecision> Decisions;
        }

        internal static IEnumerator Run(PeopleOfTheIliacBayMod host,StreetPopulation streets,string character)
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-enter-wait-probe")<0 ||
                string.IsNullOrWhiteSpace(character) || !character.StartsWith("PeopleOfTheIliacBayTest-",StringComparison.Ordinal))
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
                    "; door="+fixture.Enter.Target.DoorId+"; building="+fixture.Enter.Target.BuildingKey+".");
                var enter=store.ReadNpcAction(taskId+":step:0");
                if(enter==null || enter.Kind!=NpcActionKind.Enter || !NpcTaskContract.TargetMatches(enter.Target,fixture.Enter.Target))
                    throw new InvalidOperationException("Native Enter action was not dispatched with the selected exact doorway.");
                Vector3 door;float arrival;string resolveReason;
                if(!Resolve(streets,enter,out door,out arrival,out resolveReason))
                    throw new InvalidOperationException("Native Enter doorway could not be resolved: "+resolveReason);
                float nearestDoor=float.PositiveInfinity;NpcActionJob completedWait=null;NpcActionJob completedEnter=null;bool receiptLogged=false;
                float deadline=Time.realtimeSinceStartup+180f;
                while(Time.realtimeSinceStartup<deadline)
                {
                    if(game.PlayerEnterExit.IsPlayerInsideBuilding)
                        throw new InvalidOperationException("Enter/Wait probe player entered the building; offscreen evidence is invalid.");
                    var actor=FindActor(streets,residentId);
                    if(actor && actor.gameObject.activeInHierarchy)
                        nearestDoor=Mathf.Min(nearestDoor,Vector3.Distance(actor.transform.position-Vector3.up,door));
                    var task=store.ReadNpcTask(taskId);
                    var observedEnter=store.ReadNpcAction(taskId+":step:0");
                    var observedWait=store.ReadNpcAction(taskId+":step:1");
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
                    if(task==null || task.Phase==NpcTaskPhase.Failed || task.Phase==NpcTaskPhase.Cancelled)
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
                    (completedWait.UpdatedAt-completedWait.AcceptedAt).ToString("F3")+"; playerOutside=true; inventoryUnchanged=true; identityUnchanged=true; vitalsUnchanged=true.");
                taskId=null;
            }
            finally
            {
                if(taskId!=null && residentId!=null){string ignored;streets.TryStopConversationAction(residentId,out ignored);}
                game.PlayerMotor.transform.position=observerBefore;Physics.SyncTransforms();
            }
        }

        private static Fixture FindFixture(StreetPopulation streets,GameManager game,out string reason)
        {
            var geometry=typeof(StreetPopulation).GetField("geometry",Private).GetValue(streets) as TownGeometry;
            int eligibleAdults=0,semanticWaits=0,semanticEnters=0,namedRequests=0,namedEnters=0,boundWaits=0,invalidPlans=0;
            foreach(var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
            {
                if(!actor || !actor.gameObject.activeInHierarchy || actor.Resident==null || !actor.Resident.IsAlive ||
                    actor.Resident.IsQuestProtected || actor.Resident.NativeIdentity!=null || actor.Resident.AgeGroup!=ResidentAgeGroup.Adult ||
                    actor.IsSleeping || actor.IsConversing || streets.IsAlarmed(actor.Resident.Id))continue;
                eligibleAdults++;
                game.PlayerMotor.transform.position=actor.transform.position;Physics.SyncTransforms();
                var semanticOptions=streets.ConversationActionCandidates(actor.Resident.Id);NpcActionOption enter=null;
                foreach(var option in semanticOptions)
                {
                    if(option!=null && option.Kind==NpcActionKind.Wait && option.Target!=null)semanticWaits++;
                    if(option!=null && option.Kind==NpcActionKind.Enter && option.Target!=null &&
                        option.Target.Kind==NpcActionTargetKind.Building){enter=option;semanticEnters++;break;}
                }
                // Semantic candidates deliberately cap public buildings. For this
                // doorway probe, ask the same production offer builder for each
                // exact surveyed name so a farther open public door is not hidden
                // behind that general candidate cap.
                if(enter==null && geometry!=null)
                    foreach(var pair in geometry.Entrances)
                    {
                        string name=game.TalkManager.GetBuildingNameForBuildingKey(pair.Key);
                        if(string.IsNullOrWhiteSpace(name))continue;
                        namedRequests++;
                        var named=streets.ConversationActionOptions(actor.Resident.Id,"Please enter "+name+".");
                        foreach(var option in named)
                            if(option!=null && option.Kind==NpcActionKind.Enter && option.Target!=null &&
                                option.Target.Kind==NpcActionTargetKind.Building && option.Target.BuildingKey==pair.Key)
                            {enter=option;namedEnters++;break;}
                        if(enter!=null)break;
                    }
                if(enter==null)continue;
                int semanticMaximum=0;foreach(var option in semanticOptions)if(option!=null && option.Id>semanticMaximum)semanticMaximum=option.Id;
                int uniqueMaximum=Math.Max(semanticMaximum,enter.Id);
                var waits=NpcActionSequenceChat.BindWaitOptionsAfterMoveTo(new NpcActionDecision {Option=enter,DurationSeconds=300},semanticOptions);
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
                return new Fixture {Actor=actor,Enter=enter,Wait=wait,Decisions=new List<NpcActionDecision> {
                    new NpcActionDecision {Option=Copy(enter),DurationSeconds=300},new NpcActionDecision {Option=Copy(wait),DurationSeconds=WaitSeconds}}};
            }
            if(eligibleAdults==0)reason="No active ordinary adult is available at noon.";
            else if(semanticWaits==0)reason="Active ordinary adults offered no current native Wait option at noon.";
            else if(semanticEnters==0 && namedRequests==0)reason="No surveyed building name was available for a native public-door request.";
            else if(semanticEnters==0 && namedEnters==0)reason="Native public-door requests found no currently unlocked exact Enter option; adults="+eligibleAdults+"; semanticWaits="+semanticWaits+"; namedRequests="+namedRequests+".";
            else if(boundWaits==0)reason="Exact Enter options existed, but semantic Wait options produced no exact bound doorway Wait; semanticEnters="+semanticEnters+"; namedEnters="+namedEnters+"; semanticWaits="+semanticWaits+".";
            else reason="Exact Enter/Wait task plan was rejected; boundWaits="+boundWaits+"; invalidPlans="+invalidPlans+".";
            return null;
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
