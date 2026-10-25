using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private void OfferCompanionCastActions(ResidentActor actor,string text,List<NpcActionOption> options,bool semantic)
        {
            if(!actor || worldStore==null || options==null)return;
            foreach(var spell in NativeNpcCompanionCast.Discover(actor.gameObject,worldStore))
            {
                if(options.Count>=(semantic?NpcActionCandidateCatalog.MaximumCandidates:24))break;
                bool duplicate=false;
                foreach(var option in options)
                    if(option.CompanionCast!=null && NpcCompanionCastSpec.Same(option.CompanionCast,spell))
                    {duplicate=true;break;}
                if(duplicate)continue;
                string effect=spell.EffectKind==NpcMagicEffectKind.Health?"heal":
                    spell.EffectId.IndexOf("Poison",StringComparison.OrdinalIgnoreCase)>=0?"cure poison":
                    spell.EffectId.IndexOf("Disease",StringComparison.OrdinalIgnoreCase)>=0?"cure disease":"cast shield on";
                string recipient=spell.Delivery==NpcCompanionCastDelivery.Self?"self":"the player";
                var target=new NpcActionTarget {Kind=NpcActionTargetKind.Person,PersonId=spell.TargetId};
                options.Add(new NpcActionOption {Id=options.Count+1,Kind=NpcActionKind.CastSpell,
                    Target=target,CompanionCast=NpcCompanionCastSpec.CopyValidated(spell),
                    Label=effect+" "+recipient+" using a known spell"});
            }
        }

        private bool PrepareCompanionCastAction(ResidentActor actor,NpcActionJob job,out string reason)
        {
            reason="That exact known spell is unavailable now.";
            if(!actor || job==null || job.Kind!=NpcActionKind.CastSpell || job.CompanionCast==null ||
                job.Target==null || job.Target.Kind!=NpcActionTargetKind.Person ||
                job.Target.PersonId!=job.CompanionCast.TargetId)return false;
            foreach(var available in NativeNpcCompanionCast.Discover(actor.gameObject,worldStore))
                if(NpcCompanionCastSpec.Same(available,job.CompanionCast))
                {reason=null;return true;}
            return false;
        }

        private bool ExecuteCompanionCastAction(ResidentActor actor,NpcActionJob job,double now,out string reason)
        {
            reason="The cast could not be verified.";
            if(!actor || job==null || job.CompanionCast==null || worldStore==null)return false;
            string nativeReason=null;
            bool complete=worldStore.TryCompleteNpcActionEffect(job.Id,job.Revision,now,
                ()=>NativeNpcCompanionCast.TryExecute(actor.gameObject,worldStore,job.CompanionCast,
                    ()=>actor && actor.gameObject.activeInHierarchy && actor.Resident!=null &&
                        actor.Resident.Id==job.ResidentId && !QuestOwned(actor.Resident) &&
                        !IsAlarmed(job.ResidentId) && ActionSpaceMatches(job.LastPosition),out nativeReason));
            reason=complete?null:nativeReason??reason;
            return complete;
        }
    }
}
