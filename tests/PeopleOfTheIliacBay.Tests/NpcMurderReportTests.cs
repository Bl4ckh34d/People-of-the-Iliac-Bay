using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcMurderReportTests
    {
        private static NpcActionJob Request(WorldStateStore store)
        {
            var town=store.GetOrCreate(1,new[]{7,8},1);
            var guard=store.GetOrCreateGuards(1,1,new[]{new SavedRoutePoint()}).Guards[0];
            return new NpcActionJob {Id="report:1",ProposalId="request:1",ResidentId=town.Households[0].Residents[0].Id,
                Kind=NpcActionKind.ReportMurder,Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,
                    PersonId=guard.Id,SettlementId=1},Phase=NpcActionPhase.Accepted,Revision=1,
                AcceptedAt=100,UpdatedAt=100,DeadlineAt=900};
        }
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Anonymous murder reports require consent, actual delivery and retain hearsay after restore",()=>{
                var store=new WorldStateStore(12);var request=Request(store);string reason;
                Check.True(!ClassifiedAdmissionFixture.TryAcceptAction(store,request,true,false,true,out reason));
                Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store,request,true,true,true,out reason),reason);
                string id=request.Id,guard=request.Target.PersonId;
                Check.True(!store.TryDeliverNpcMurderReport(id,1,101,guard,1,true));
                Check.True(store.ObserveNpcAction(id,1,101,NpcActionObservation.Arrival()));
                Check.True(!store.ObserveNpcAction(id,2,102,NpcActionObservation.Success("fake-arrival")));
                Check.True(!store.TryCompleteNpcActionEffect(id,2,102,()=>true));
                Check.True(!store.TryDeliverNpcMurderReport(id,2,102,guard,8,true));
                Check.True(!store.TryDeliverNpcMurderReport(id,2,102,guard,double.NaN,true));
                Check.True(!store.TryDeliverNpcMurderReport(id,2,102,guard,1,false));
                Check.True(!store.TryDeliverNpcMurderReport(id,2,102,"other-guard",1,true));
                Check.Equal(0,store.Snapshot().SceneMemories.Count);
                var pending=new WorldStateStore(5);pending.Restore(store.Snapshot());store=pending;
                Check.Equal(NpcActionPhase.Executing,store.ReadNpcAction(id).Phase);
                Check.True(store.TryDeliverNpcMurderReport(id,2,102,guard,1,true));
                Check.True(!store.TryDeliverNpcMurderReport(id,2,103,guard,1,true));
                Check.Equal(NpcActionPhase.Completed,store.ReadNpcAction(id).Phase);
                var restored=new WorldStateStore(5);restored.Restore(store.Snapshot());
                var memories=restored.Snapshot().SceneMemories;
                Check.Equal(2,memories.Count);
                Check.True(memories.TrueForAll(m=>m.Kind==NpcSceneEventKind.Conversation && m.ResponsibleId==null));
                var heard=memories.Find(m=>m.ObserverId==guard);
                Check.True(heard.Observation.Contains("unverified hearsay"));
                Check.True(heard.SubjectId==null && !heard.Observation.Contains("player"),"Guard must not learn the anonymous informant's identity.");
                Check.True(restored.RecallSceneEvents(request.ResidentId,103).Contains("player's anonymous report"));
                Check.Equal(0,restored.GetGuards(1).Reports.Count);
            }));
            cases.Add(("Dead guard or cancelled report cannot receive hearsay",()=>{
                var store=new WorldStateStore(12);var request=Request(store);string reason;
                Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store,request,true,true,true,out reason),reason);
                Check.True(store.ObserveNpcAction(request.Id,1,101,NpcActionObservation.Arrival()));
                store.GetGuards(1).Kill(request.Target.PersonId,102);
                Check.True(!store.TryDeliverNpcMurderReport(request.Id,2,102,request.Target.PersonId,1,true));
                Check.True(store.ObserveNpcAction(request.Id,2,103,NpcActionObservation.Cancelled("Player cancelled.")));
                Check.True(!store.TryDeliverNpcMurderReport(request.Id,3,104,request.Target.PersonId,1,true));
                Check.Equal(0,store.Snapshot().SceneMemories.Count);
            }));
        }
    }
}
