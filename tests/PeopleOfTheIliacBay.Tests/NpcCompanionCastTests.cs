using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.Tests
{
    internal static class NpcCompanionCastTests
    {
        public static void Add(List<(string Name,Action Body)> cases)
        {
            cases.Add(("Known companion spell offer binds exact spell, target and cost",OfferBindsExactSpell));
            cases.Add(("Companion spell job rejects wrong target and dual care ownership",InvalidJobsFailClosed));
            cases.Add(("Accepted companion spell survives save and completes only after synchronous effect",ActionCompletesAfterEffect));
        }

        private static NpcCompanionCastSpec Spec(string target="player",NpcCompanionCastDelivery delivery=NpcCompanionCastDelivery.PlayerTouch)
        {return new NpcCompanionCastSpec {SpellId="native:spellbook:0:exact-hash",EffectId="HealHealth",
            EffectKind=NpcMagicEffectKind.Health,TargetId=target,Delivery=delivery,MagickaCost=7};}

        private static NpcActionJob Job(string residentId)
        {return new NpcActionJob {Id="companion-cast:test",ProposalId="companion-cast:test",ResidentId=residentId,
            Kind=NpcActionKind.CastSpell,Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player"},
            CompanionCast=Spec(),Phase=NpcActionPhase.Accepted,Revision=1,AcceptedAt=100,UpdatedAt=100,DeadlineAt=200};}

        private static void OfferBindsExactSpell()
        {
            var option=new NpcActionOption {Id=1,Kind=NpcActionKind.CastSpell,
                Target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId="player"},CompanionCast=Spec()};
            var parsed=NpcActionChat.ParseSemantic("DIRECT|CastSpell|1|1|45",new[]{option});
            Check.True(parsed!=null && NpcCompanionCastSpec.Same(parsed.Option.CompanionCast,option.CompanionCast));
            IList<NpcActionOption> compact;int characters;
            Check.True(NpcActionCandidateCatalog.TryCreateCompact(new[]{option},out compact,out characters));
            Check.True(NpcCompanionCastSpec.Same(compact[0].CompanionCast,option.CompanionCast));
            compact[0].CompanionCast.MagickaCost=11;
            Check.Equal(7,option.CompanionCast.MagickaCost);
            var offer=new NpcActionOffer("resident",3,100,new NpcActionDecision {Option=option,DurationSeconds=45});
            option.CompanionCast.MagickaCost=99;
            NpcActionOption detached;int seconds;NpcActionDecision accepted;
            Check.True(offer.TryGetSemanticCandidate("resident",3,101,out detached,out seconds));
            Check.Equal(7,detached.CompanionCast.MagickaCost);
            var changed=new NpcActionDecision {Option=detached,DurationSeconds=seconds};
            changed.Option.CompanionCast.SpellId="different";
            Check.True(!offer.TryAcceptSemantic("resident",3,102,changed,out accepted));
            Check.True(offer.TryGetSemanticCandidate("resident",3,103,out detached,out seconds));
            Check.True(offer.TryAcceptSemantic("resident",3,104,
                new NpcActionDecision {Option=detached,DurationSeconds=seconds},out accepted));
            Check.Equal("native:spellbook:0:exact-hash",accepted.Option.CompanionCast.SpellId);
            Check.True(!offer.TryAcceptSemantic("resident",3,105,
                new NpcActionDecision {Option=detached,DurationSeconds=seconds},out accepted));
        }

        private static void InvalidJobsFailClosed()
        {
            var job=Job("resident");NpcActionExecutor.Validate(job);
            job.Target.PersonId="other";
            Check.True(Rejects(job));
            job.Target.PersonId="player";
            job.CompanionCast.TargetId="other";
            Check.True(Rejects(job));
            job.CompanionCast=Spec("resident",NpcCompanionCastDelivery.Self);
            job.Target.PersonId="resident";
            NpcActionExecutor.Validate(job);
            job.CompanionCast.TargetId="other";
            Check.True(Rejects(job));
            job.CompanionCast=Spec();job.Target.PersonId="player";
            job.MagicHealing=new NpcMagicHealingSpec();
            Check.True(Rejects(job));
        }

        private static bool Rejects(NpcActionJob job)
        {try {NpcActionExecutor.Validate(job);return false;}catch(ArgumentException){return true;}}

        private static void ActionCompletesAfterEffect()
        {
            var store=new WorldStateStore(42);
            string resident=store.GetOrCreate(100,new[]{10},1).Households[0].Residents[0].Id;
            var job=Job(resident);string reason;
            Check.True(ClassifiedAdmissionFixture.TryAcceptAction(store,job,true,true,true,out reason),reason);
            job=store.ReadNpcAction(job.Id);
            Check.True(job.CompanionCast!=null && job.CompanionCast.SpellId==Spec().SpellId);
            var restored=new WorldStateStore(9);restored.Restore(store.Snapshot());
            job=restored.ReadNpcAction(job.Id);
            Check.True(job.CompanionCast!=null && NpcCompanionCastSpec.Same(job.CompanionCast,Spec()));
            Check.True(restored.ObserveNpcAction(job.Id,job.Revision,101,NpcActionObservation.Approach()));
            job=restored.ReadNpcAction(job.Id);
            Check.True(restored.ObserveNpcAction(job.Id,job.Revision,102,NpcActionObservation.Arrival()));
            job=restored.ReadNpcAction(job.Id);
            int calls=0;
            Check.True(!restored.TryCompleteNpcActionEffect(job.Id,job.Revision,103,()=>{calls++;return false;}));
            Check.Equal(1,calls);
            Check.Equal(NpcActionPhase.Executing,restored.ReadNpcAction(job.Id).Phase);
            Check.True(restored.TryCompleteNpcActionEffect(job.Id,job.Revision,104,()=>{calls++;return true;}));
            Check.Equal(2,calls);
            Check.Equal(NpcActionPhase.Completed,restored.ReadNpcAction(job.Id).Phase);
            Check.True(!restored.TryCompleteNpcActionEffect(job.Id,job.Revision,105,()=>{calls++;return true;}));
            Check.Equal(2,calls);
        }
    }
}
