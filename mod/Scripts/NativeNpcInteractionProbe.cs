using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public static class NativeNpcInteractionProbe
    {
        private const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
        private sealed class Candidate
        {
            public ResidentActor Source,Target;
            public Vector3 PlayerPoint;
            public NpcActionOption Option;
        }
        private sealed class MovementTrace
        {
            public Vector3 Last;
            public float Distance,MaximumStep;
            public MovementTrace(Vector3 start){Last=start;}
            public void Sample(Vector3 point)
            {
                float step=Vector3.Distance(Last,point);Distance+=step;MaximumStep=Math.Max(MaximumStep,step);Last=point;
            }
        }

        private static T Field<T>(object owner,string name)
        {return (T)typeof(StreetPopulation).GetField(name,Private).GetValue(owner);}

        private static bool Eligible(ResidentActor actor)
        {
            return actor && actor.Person && actor.gameObject.activeInHierarchy && actor.Resident.IsAlive && !actor.Resident.IsQuestProtected &&
                actor.Resident.AgeGroup==ResidentAgeGroup.Adult && !actor.IsSleeping && !actor.IsConversing;
        }

        private static bool TryFindCandidate(StreetPopulation streets,ResidentActor[] actors,TownGeometry geometry,
            Transform townOrigin,WorldStateStore store,out Candidate result)
        {
            result=null;
            var game=GameManager.Instance;
            var player=GameManager.Instance.PlayerMotor.transform.position-Vector3.up;
            foreach(var source in actors)
            {
                if(!Eligible(source)) continue;
                var sourceLocal=townOrigin.InverseTransformPoint(source.transform.position-Vector3.up);
                Vector3 staged;
                if(!TryStagePoint(source,actors,geometry,townOrigin,sourceLocal,out staged)) continue;
                foreach(var target in actors)
                {
                    if(target==source || !Eligible(target) || source.gameObject.scene!=target.gameObject.scene) continue;
                    var active=store.ActiveNpcAction(target.Resident.Id);
                    if(active!=null && (!NpcActionExecutor.IsTerminal(active) || active.ReturningToRoutine) ||
                        store.ActiveNpcTask(target.Resident.Id)!=null || streets.IsAlarmed(target.Resident.Id)) continue;
                    float distance=Vector3.Distance(source.transform.position-Vector3.up,target.transform.position-Vector3.up);
                    if(distance<2f || distance>20f) continue;
                    game.PlayerMotor.transform.position=staged+Vector3.up;
                    Physics.SyncTransforms();
                    string request="Please talk to "+target.Person.NameNPC+".";
                    var options=streets.ConversationActionOptions(source.Resident.Id,request);
                    foreach(var option in options)
                        if(option.Kind==NpcActionKind.Interact && option.Target!=null && option.Target.PersonId==target.Resident.Id)
                        {result=new Candidate {Source=source,Target=target,PlayerPoint=staged,Option=option};return true;}
                }
            }
            game.PlayerMotor.transform.position=player;
            return false;
        }

        private static bool TryStagePoint(ResidentActor source,ResidentActor[] actors,TownGeometry geometry,
            Transform townOrigin,Vector3 sourceLocal,out Vector3 staged)
        {
            staged=Vector3.zero;
            var from=new SavedRoutePoint {X=sourceLocal.x,Z=sourceLocal.z};
            for(int ring=0;ring<5;ring++)
            {
                float radius=2.5f+ring*0.7f;
                for(int direction=0;direction<16;direction++)
                {
                    double angle=direction*Math.PI/8.0;
                    var local=new Vector3(sourceLocal.x+(float)Math.Cos(angle)*radius,sourceLocal.y,
                        sourceLocal.z+(float)Math.Sin(angle)*radius);
                    var point=geometry.ScenePoint(new RoutePoint(local.x,local.z));
                    if(Math.Abs(point.y-source.transform.position.y)>0.5f ||
                        Vector3.Distance(point,source.transform.position)<2.5f ||
                        Vector3.Distance(point,source.transform.position)>6.5f) continue;
                    bool occupied=false;
                    foreach(var actor in actors)
                        if(actor && actor!=source && actor.gameObject.activeInHierarchy &&
                            Vector3.Distance(point,actor.transform.position)<1.5f) {occupied=true;break;}
                    if(occupied) continue;
                    var to=new SavedRoutePoint {X=local.x,Z=local.z};
                    if(geometry.RouteBetweenPoints(from,to).Count<2) continue;
                    staged=point;return true;
                }
            }
            return false;
        }

        private static NpcMemoryEntry PendingEntry(WorldState state,string personId,int index)
        {
            var journal=state.ConversationMemories.Find(value=>value.PersonId==personId);
            return journal==null || journal.Entries.Count!=index+1?null:journal.Entries[index];
        }

        private static int PendingCount(WorldState state,string personId)
        {
            var journal=state.ConversationMemories.Find(value=>value.PersonId==personId);
            return journal==null?0:journal.Entries.Count;
        }

        private static bool IsExactObservation(NpcMemoryEntry entry,string otherId)
        {
            return entry!=null && entry.Source=="observed" && entry.PlayerText=="" && entry.NpcText=="" &&
                entry.Observation.Contains(otherId) && entry.Observation.Contains("No spoken exchange was observed.");
        }

        public static IEnumerator Run(StreetPopulation streets,string expectedCharacter)
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-peopleoftheiliacbay-npc-interaction-probe")<0 ||
                string.IsNullOrWhiteSpace(expectedCharacter) || !expectedCharacter.StartsWith("PeopleOfTheIliacBayTest-",StringComparison.Ordinal))
                throw new InvalidOperationException("NPC interaction acceptance requires its explicit owned-fixture flag.");
            var game=GameManager.Instance;
            if(!streets || !streets.Ready || game.PlayerEntity.Name!=expectedCharacter || game.IsPlayerInside)
                throw new InvalidOperationException("NPC interaction probe requires the owned character in a loaded exterior town.");
            var actorsMap=Field<Dictionary<string,ResidentActor>>(streets,"actors");
            var geometry=Field<TownGeometry>(streets,"geometry");
            var directory=Field<BuildingDirectory>(streets,"directory");
            var store=Field<WorldStateStore>(streets,"worldStore");
            if(geometry==null || directory==null || store==null || geometry.Entrances.Count==0)
                throw new InvalidOperationException("NPC interaction probe requires surveyed town navigation and entrances.");
            var originalPlayer=game.PlayerMotor.transform.position;
            try
            {
                Candidate selected=null;
                float setupDeadline=Time.realtimeSinceStartup+20;
                while(selected==null && Time.realtimeSinceStartup<setupDeadline)
                {
                    var actors=new List<ResidentActor>(actorsMap.Values).ToArray();
                    TryFindCandidate(streets,actors,geometry,directory.transform,store,out selected);
                    if(selected==null) yield return new WaitForSecondsRealtime(0.25f);
                }
                if(selected==null) throw new InvalidOperationException("No existing eligible named resident pair had a checked player point and normal Interact offer.");
                var source=selected.Source;var target=selected.Target;
                if(!Eligible(source) || !Eligible(target) || source.gameObject.scene!=target.gameObject.scene)
                    throw new InvalidOperationException("Selected named resident pair changed eligibility before acceptance.");
                string request="Please talk to "+target.Person.NameNPC+".";
                var options=streets.ConversationActionOptions(source.Resident.Id,request);
                var offered=options.Find(option=>option.Kind==NpcActionKind.Interact && option.Target.PersonId==target.Resident.Id);
                if(offered==null) throw new InvalidOperationException("Normal Interact offer disappeared before typed acceptance.");
                var decision=NpcActionChat.ParseSemantic("DIRECT|Interact|"+offered.Id+"|"+offered.Target.Quantity+"|60",options);
                if(decision==null) throw new InvalidOperationException("Typed Interact decision did not resolve from the live offer.");
                int session=101;
                double offeredAt=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                var offer=new NpcActionOffer(source.Resident.Id,session,offeredAt,decision);
                NpcActionDecision typedAcceptance;
                if(!offer.TryAcceptSemantic(source.Resident.Id,session,offeredAt+1,decision,out typedAcceptance))
                    throw new InvalidOperationException("Typed yes did not accept the live Interact offer.");
                var beforeState=store.Snapshot();
                int sourceMemoryCount=PendingCount(beforeState,source.Resident.Id);
                int targetMemoryCount=PendingCount(beforeState,target.Resident.Id);
                Vector3 start=source.transform.position-Vector3.up;
                Vector3 originalTarget=target.transform.position-Vector3.up;
                if(Vector3.Distance(start,originalTarget)<=1.5f)
                    throw new InvalidOperationException("Selected resident pair is already in arrival range; physical approach would not be measured.");
                string reason;
                string proposal="native-interact:"+Guid.NewGuid().ToString("N");
                if(!NpcProbeCommandSource.TryAcceptAction(streets,source.Resident.Id,proposal,typedAcceptance,out reason))
                    throw new InvalidOperationException("Normal typed Interact acceptance failed: "+reason);
                var accepted=store.ActiveNpcAction(source.Resident.Id);
                if(accepted==null || accepted.Kind!=NpcActionKind.Interact || accepted.Target.PersonId!=target.Resident.Id)
                    throw new InvalidOperationException("Accepted action did not preserve exact named receiver identity.");
                var trace=new MovementTrace(source.transform.position);
                float actionDeadline=Time.realtimeSinceStartup+120;
                NpcActionJob outcome=accepted;
                while(Time.realtimeSinceStartup<actionDeadline)
                {
                    trace.Sample(source.transform.position);
                    outcome=store.ReadNpcAction(accepted.Id);
                    if(outcome==null || NpcActionExecutor.IsTerminal(outcome)) break;
                    yield return null;
                }
                trace.Sample(source.transform.position);
                if(outcome==null || outcome.Phase!=NpcActionPhase.Completed || !outcome.InteractionMemoryRecorded)
                    throw new InvalidOperationException("Physical Interact did not complete with reciprocal memory receipt: "+
                        (outcome==null?"missing":outcome.Phase+" "+outcome.FailureReason));
                float finalGap=Vector3.Distance(source.transform.position-Vector3.up,target.transform.position-Vector3.up);
                if(trace.Distance<0.5f || trace.MaximumStep>0.5f || finalGap>1.35f || !Eligible(target) ||
                    !CivilianResponses.Sees(source.transform.position,target.transform.position,2.35f) ||
                    !CivilianResponses.Sees(target.transform.position,source.transform.position,2.35f))
                    throw new InvalidOperationException("Completed Interact lacked measured movement, close receiver, or mutual sight (walk="+
                        trace.Distance.ToString("0.00")+"m, maxStep="+trace.MaximumStep.ToString("0.00")+"m, gap="+finalGap.ToString("0.00")+"m).");
                var afterState=store.Snapshot();
                if(PendingCount(afterState,source.Resident.Id)!=sourceMemoryCount+1 ||
                    PendingCount(afterState,target.Resident.Id)!=targetMemoryCount+1)
                    throw new InvalidOperationException("Successful Interact did not append exactly one pending memory per person.");
                var sourceEntry=PendingEntry(afterState,source.Resident.Id,sourceMemoryCount);
                var targetEntry=PendingEntry(afterState,target.Resident.Id,targetMemoryCount);
                if(!IsExactObservation(sourceEntry,target.Resident.Id) || !IsExactObservation(targetEntry,source.Resident.Id))
                    throw new InvalidOperationException("Reciprocal memories contained dialogue or lacked exact observed identities.");

                string encoded=SaveLoadManager.Serialize(typeof(WorldState),afterState,false);
                var restoredState=(WorldState)SaveLoadManager.Deserialize(typeof(WorldState),encoded);
                var serializedJob=restoredState.NpcActions.Find(job=>job.Id==accepted.Id);
                if(serializedJob==null || !serializedJob.InteractionMemoryRecorded ||
                    PendingCount(restoredState,source.Resident.Id)!=sourceMemoryCount+1 ||
                    PendingCount(restoredState,target.Resident.Id)!=targetMemoryCount+1)
                    throw new InvalidOperationException("Native serializer lost interaction receipt or either factual memory.");
                var restored=new WorldStateStore(restoredState.WorldSeed,PeopleOfTheIliacBayMod.ConversationArchiveRoot());
                restored.Restore(restoredState);
                var saved=restored.ReadNpcAction(accepted.Id);
                if(saved==null || !saved.InteractionMemoryRecorded ||
                    restored.TryRecordNpcInteractionMemory(saved.Id,saved.Revision,saved.UpdatedAt,"native retry","native retry",out _))
                    throw new InvalidOperationException("Restored interaction receipt permitted duplicate memory append.");
                var afterRetry=restored.Snapshot();
                if(PendingCount(afterRetry,source.Resident.Id)!=sourceMemoryCount+1 ||
                    PendingCount(afterRetry,target.Resident.Id)!=targetMemoryCount+1)
                    throw new InvalidOperationException("Duplicate retry changed memory counts.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_INTERACTION_PASS: source="+source.Resident.Id+"; target="+target.Resident.Id+
                    "; walk="+trace.Distance.ToString("0.00")+"m; maxStep="+trace.MaximumStep.ToString("0.00")+
                    "m; finalGap="+finalGap.ToString("0.00")+"m; mutualLOS=pass; pairedObserved=pass; spokenText=none; "+
                    "nativeSerializer=pass; restoredRetry=blocked; diskReload=not-tested");
            }
            finally
            {
                game.PlayerMotor.transform.position=originalPlayer;
                Physics.SyncTransforms();
            }
        }
    }
}
