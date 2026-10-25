using System;
using System.Collections.Generic;
using System.Reflection;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcRoutineRecoveryTransitionTests
    {
        private static NpcActionTarget Outside(double x=0) => new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,
            SettlementId=100,WorldSpaceId="settlement:100",X=x};
        private static NpcActionTarget Inside(int building=10) => new NpcActionTarget {Kind=NpcActionTargetKind.IndoorPoint,
            SettlementId=100,BuildingKey=building};

        private static WorldStateStore Fixture(out NpcActionJob job)
        {
            var store=new WorldStateStore(42);var town=store.GetOrCreate(100,new[]{10},1);
            job=new NpcActionJob {Id="errand:1",ProposalId="turn:errand:1",ResidentId=town.Households[0].Residents[0].Id,
                Kind=NpcActionKind.MoveTo,Target=Outside(20),Phase=NpcActionPhase.Accepted,Revision=1,
                AcceptedAt=100,UpdatedAt=100,DeadlineAt=500,LastPosition=Outside()};
            string reason;Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store, job,true,true,true,out reason),reason);
            job=store.ReadNpcAction(job.Id);
            Check.True(store.ObserveNpcAction(job.Id,job.Revision,101,NpcActionObservation.Approach()));
            job=store.ReadNpcAction(job.Id);
            Check.True(store.ObserveNpcAction(job.Id,job.Revision,102,NpcActionObservation.Interrupted("Action ended; return to routine.")));
            job=store.ReadNpcAction(job.Id);
            Check.True(store.SaveNpcActionPosition(job.Id,job.Revision,Outside(),true,103));
            job=store.ReadNpcAction(job.Id);return store;
        }

        private static bool Call(WorldStateStore store,string name,params object[] args)
        {
            var method=typeof(WorldStateStore).GetMethod(name);
            Check.True(method!=null,"Routine recovery needs persisted, revision-checked scene handoff.");
            try{return (bool)method.Invoke(store,args);}catch(TargetInvocationException error){throw error.InnerException;}
        }

        private static object Transition(NpcActionJob job)
        {
            var field=typeof(NpcActionJob).GetField("RoutineRecoveryTransition");
            Check.True(field!=null,"Errand action needs a distinct routine recovery transition field.");
            return field.GetValue(job);
        }

        private static bool Begin(WorldStateStore store,NpcActionJob job,IList<NpcActionTarget> route=null)
        {
            return Call(store,"TryBeginNpcRoutineRecoveryTransition",job.Id,job.Revision,110d,Inside(),
                route??new List<NpcActionTarget>{Outside(),Outside(10)},2d,10);
        }

        private static bool BeginOutdoorJoin(WorldStateStore store,NpcActionJob job,int origin,NpcActionTarget destination)
        {
            try{return Call(store,"TryBeginNpcRoutineRecoveryTransition",job.Id,job.Revision,110d,destination,
                new List<NpcActionTarget>{Outside(),Outside(destination.X)},2d,origin);}
            catch(TargetParameterCountException){return false;}
        }

        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Generated guest keeps return ownership through offscreen Inn approach and native entry after restore",()=>{
                var store=new WorldStateStore(42);store.GetOrCreate(100,new int[0]);
                var guest=TravelerDemand.Create(42,100,20,0,0,new[]{new TravelerOrigin{SettlementId=200,Race=1}},1,1,false,
                    new[]{new RoutePoint(0,0),new RoutePoint(10,0)},1,(origin,seed,sex)=>"Actual Inn Guest");
                store.RegisterTraveler(guest.Person);store.RegisterTravelerVisit(guest.Visit);
                var job=new NpcActionJob {Id="guest-return",ProposalId="guest-return",ResidentId=guest.Person.Id,
                    Kind=NpcActionKind.Follow,Target=new NpcActionTarget{Kind=NpcActionTargetKind.Person,PersonId="player"},
                    Phase=NpcActionPhase.Accepted,Revision=1,AcceptedAt=100,UpdatedAt=100,FinishAt=400,DeadlineAt=460,
                    LastPosition=Outside()};
                string reason;Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store,job,true,true,true,out reason),reason);
                Check.True(store.ObserveNpcAction(job.Id,1,101,NpcActionObservation.Cancelled("Dismissed.")));
                job=store.ReadNpcAction(job.Id);
                Check.True(store.SaveNpcActionPosition(job.Id,job.Revision,Outside(),true,103));job=store.ReadNpcAction(job.Id);
                Check.True(store.TryBeginNpcRoutineRecoveryTransition(job.Id,job.Revision,110,Outside(10),
                    new[]{Outside(),Outside(10)},2,20));
                var copy=new WorldStateStore(0);copy.Restore(store.Snapshot());job=copy.ReadNpcAction(job.Id);
                Check.True(!copy.TryArriveNpcRoutineRecoveryTransition(job.Id,job.Revision,129,Outside(10)));
                Check.True(copy.TryArriveNpcRoutineRecoveryTransition(job.Id,job.Revision,130,Outside(10)));
                job=copy.ReadNpcAction(job.Id);Check.True(job.ReturningToRoutine);
                Check.Equal(-1,copy.ResolveResident(100,guest.Person.Id).HomeKey);
                Check.True(copy.TryBeginNpcRoutineRecoveryTransition(job.Id,job.Revision,131,Inside(20),
                    new[]{Outside(10),Outside(10)},2,20));
                var entered=new WorldStateStore(0);entered.Restore(copy.Snapshot());job=entered.ReadNpcAction(job.Id);
                Check.True(!entered.TryArriveNpcRoutineRecoveryTransition(job.Id,job.Revision,132,Inside(999)));
                Check.True(entered.TryArriveNpcRoutineRecoveryTransition(job.Id,job.Revision,132,Inside(20)));
                job=entered.ReadNpcAction(job.Id);
                Check.True(job.ReturningToRoutine && job.RoutineRecoveryTransition==null);
                Check.Equal(20,job.LastPosition.BuildingKey);Check.Equal(guest.Person.Id,job.ResidentId);
                Check.Equal(-1,entered.ResolveResident(100,guest.Person.Id).HomeKey);
                Check.True(entered.SaveNpcActionPosition(job.Id,job.Revision,Inside(20),false,133));
                Check.True(entered.ActiveNpcAction(guest.Person.Id)==null);
            }));
            cases.Add(("Terminal non-follow errand preserves return ownership and elapsed source route across save restore",()=>{
                NpcActionJob job;var store=Fixture(out job);var route=new List<NpcActionTarget>{Outside(),Outside(10)};
                Check.True(Begin(store,job,route));route[1].X=999;
                var pending=store.ReadNpcAction(job.Id);Check.True(pending.ReturningToRoutine);
                Check.Equal(NpcActionKind.MoveTo,pending.Kind);Check.Equal(NpcActionPhase.Failed,pending.Phase);
                var transit=Transition(pending);Check.True(transit!=null);
                var source=(List<NpcActionTarget>)transit.GetType().GetField("SourceRoute").GetValue(transit);
                Check.Near(10,source[1].X);
                Check.Near(130,(double)transit.GetType().GetField("ArrivesAt").GetValue(transit));
                Check.True(!store.SaveNpcActionPosition(job.Id,pending.Revision,Outside(4),true,115),
                    "Position save overwrote measured scene handoff.");
                var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());pending=restored.ReadNpcAction(job.Id);
                Check.True(!Call(restored,"TryArriveNpcRoutineRecoveryTransition",job.Id,pending.Revision,129d,Inside()));
                Check.True(Call(restored,"TryArriveNpcRoutineRecoveryTransition",job.Id,pending.Revision,130d,Inside()));
                var arrived=restored.ReadNpcAction(job.Id);
                Check.Equal(NpcActionKind.MoveTo,arrived.Kind);Check.Equal(NpcActionPhase.Failed,arrived.Phase);
                Check.True(arrived.ReturningToRoutine);Check.True(Transition(arrived)==null);
                Check.Equal(NpcActionTargetKind.IndoorPoint,arrived.LastPosition.Kind);
                Check.Equal(10,arrived.LastPosition.BuildingKey);
            }));
            cases.Add(("Failed native routine doorway handoff resumes at measured source position",()=>{
                NpcActionJob job;var store=Fixture(out job);Check.True(Begin(store,job));job=store.ReadNpcAction(job.Id);
                Check.True(Call(store,"TryResumeNpcRoutineRecoverySource",job.Id,job.Revision,120d));
                var resumed=store.ReadNpcAction(job.Id);
                Check.True(resumed.ReturningToRoutine);Check.True(Transition(resumed)==null);
                Check.Equal(NpcActionPhase.Failed,resumed.Phase);Check.Equal(NpcActionTargetKind.WorldPoint,resumed.LastPosition.Kind);
                Check.Near(5,resumed.LastPosition.X);
            }));
            cases.Add(("Routine recovery scene handoff rejects unmeasured route and stale ownership",()=>{
                NpcActionJob job;var store=Fixture(out job);
                Check.True(!Begin(store,job,new List<NpcActionTarget>{Outside(1),Outside(10)}));
                Check.True(Begin(store,job));job=store.ReadNpcAction(job.Id);
                Check.True(!Call(store,"TryBeginNpcRoutineRecoveryTransition",job.Id,job.Revision,111d,Inside(),
                    new List<NpcActionTarget>{Outside(),Outside(10)},2d,10));
            }));
            cases.Add(("Player entry preserves returning errand route to its own saved routine door",()=>{
                NpcActionJob job;var store=Fixture(out job);var ownDoor=Outside(30);
                Check.True(BeginOutdoorJoin(store,job,20,ownDoor),"A measured same-world join to the NPC's own routine node must persist.");
                var pending=store.ReadNpcAction(job.Id);var transit=Transition(pending);
                Check.Equal(20,(int)transit.GetType().GetField("OriginBuildingKey").GetValue(transit));
                Check.Equal(30,pending.RoutineRecoveryTransition.Destination.X);
                Check.Near(170,pending.RoutineRecoveryTransition.ArrivesAt);
                var restored=new WorldStateStore(0);restored.Restore(store.Snapshot());pending=restored.ReadNpcAction(job.Id);
                Check.Equal(20,(int)Transition(pending).GetType().GetField("OriginBuildingKey").GetValue(Transition(pending)));
                Check.True(Call(restored,"TryArriveNpcRoutineRecoveryTransition",job.Id,pending.Revision,170d,ownDoor));
                pending=restored.ReadNpcAction(job.Id);Check.True(pending.ReturningToRoutine);
                Check.True(Transition(pending)==null);Check.Near(30,pending.LastPosition.X);
                job=store.ReadNpcAction(job.Id);Check.True(Call(store,"TryResumeNpcRoutineRecoverySource",job.Id,job.Revision,115d));
                job=store.ReadNpcAction(job.Id);
                store=Fixture(out job);Check.True(!BeginOutdoorJoin(store,job,20,Inside(999)),
                    "Player's arbitrary interior door cannot become NPC's return destination.");
            }));
        }
    }
}
