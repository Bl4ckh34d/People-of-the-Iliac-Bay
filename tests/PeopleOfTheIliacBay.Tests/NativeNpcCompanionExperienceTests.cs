using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NativeNpcCompanionExperienceTests
    {
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Successful bound Follow use awards only its native skill", BoundFollowAwardsNativeSkill));
            cases.Add(("Ordinary resident actions and invalid native skills gain no companion experience", UnboundOrInvalidUseDoesNotAward));
            cases.Add(("Cancelled Follow returning to routine grants no companion experience", CancelledFollowReturnDoesNotAward));
            cases.Add(("Expired Follow grants no companion experience", ExpiredFollowDoesNotAward));
        }

        private static void BoundFollowAwardsNativeSkill()
        {
            string resident;var store=CreateStore(out resident);
            var follow=Job(resident,NpcActionKind.Follow,100);
            string reason;
            Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store,follow,true,true,true,out reason),reason);
            int before=store.ReadNpcActor(resident,store.ResolveNpcArchetype(resident)).Skill(20),gain;
            Check.True(NativeNpcCompanionExperience.TryAwardSuccessfulUse(store,resident,20,101,out gain));
            Check.Equal(store.ReadNpcActor(resident,store.ResolveNpcArchetype(resident)).Skill(20)-before,gain);
            var progress=store.ReadNpcProgress(resident);
            Check.Equal(1,progress.Skills.Count);
            Check.Equal(20,progress.Skills[0].Skill);
            Check.Equal(1L,progress.Skills[0].Experience);
        }

        private static void UnboundOrInvalidUseDoesNotAward()
        {
            string resident;var store=CreateStore(out resident);
            var wait=Job(resident,NpcActionKind.Wait,100);
            string reason;
            Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store,wait,true,true,true,out reason),reason);
            Check.True(!NativeNpcCompanionExperience.TryAwardSuccessfulUse(store,resident,20,101));
            Check.True(!NativeNpcCompanionExperience.TryAwardSuccessfulUse(store,resident,-1,101));
            Check.Equal(null,store.ReadNpcProgress(resident));
        }

        private static void CancelledFollowReturnDoesNotAward()
        {
            string resident;var store=CreateStore(out resident);
            var follow=Job(resident,NpcActionKind.Follow,100);
            string reason;
            Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store,follow,true,true,true,out reason),reason);
            Check.True(store.ObserveNpcAction(follow.Id,1,101,NpcActionObservation.Cancelled("Stop requested.")),"Follow cancellation failed.");
            Check.True(store.SaveNpcActionPosition(follow.Id,2,ReturnPosition(),true,102),"Follow return position save failed.");
            var returning=store.ActiveNpcAction(resident);
            Check.True(returning!=null && returning.ReturningToRoutine && NpcActionExecutor.IsTerminal(returning),"Terminal Follow return did not remain action-index active.");
            Check.True(!NativeNpcCompanionExperience.TryAwardSuccessfulUse(store,resident,20,102));
            Check.Equal(null,store.ReadNpcProgress(resident));
        }

        private static void ExpiredFollowDoesNotAward()
        {
            string resident;var store=CreateStore(out resident);
            var follow=Job(resident,NpcActionKind.Follow,100);
            string reason;
            Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store,follow,true,true,true,out reason),reason);
            Check.True(!NativeNpcCompanionExperience.TryAwardSuccessfulUse(store,resident,20,200));
            Check.Equal(null,store.ReadNpcProgress(resident));
        }

        private static WorldStateStore CreateStore(out string resident)
        {
            var store=new WorldStateStore(42);
            resident=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0].Id;
            return store;
        }

        private static NpcActionJob Job(string resident,NpcActionKind kind,double at)
        {
            return new NpcActionJob {Id="companion-experience:"+kind,ProposalId="companion-experience:"+kind,
                ResidentId=resident,Kind=kind,Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player"},
                Phase=NpcActionPhase.Accepted,Revision=1,AcceptedAt=at,UpdatedAt=at,DeadlineAt=at+100};
        }

        private static NpcActionTarget ReturnPosition()
        {return new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,SettlementId=100,WorldSpaceId="settlement:100"};}
    }
}
