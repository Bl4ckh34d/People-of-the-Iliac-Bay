using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // One known helpful spell, one exact recipient, and one synchronous native result.
    public static class NativeNpcCompanionCast
    {
        private const int MaxSpells=128;

        public static NpcCompanionCastSpec[] Discover(GameObject actor,WorldStateStore store)
        {
            try
            {
                DaggerfallEntityBehaviour body;EntityEffectManager manager;NativeNpcVitalsBinding binding;
                ResidentActor resident;
                if(!TryActor(actor,store,out body,out manager,out binding,out resident) || manager.HasReadySpell)
                    return new NpcCompanionCastSpec[0];
                int health,maxHealth,magicka,maxMagicka;
                if(!CurrentVitals(store,body,binding,resident,out health,out maxHealth,out magicka,out maxMagicka))
                    return new NpcCompanionCastSpec[0];
                int count=body.Entity.SpellbookCount();
                if(count<0 || count>MaxSpells)return new NpcCompanionCastSpec[0];
                var result=new List<NpcCompanionCastSpec>();
                for(int i=0;i<count;i++)
                {
                    EffectBundleSettings settings;
                    if(!body.Entity.GetSpell(i,out settings))continue;
                    NpcCompanionCastSpec spec;
                    if(!Describe(body,manager,resident.Resident.Id,settings,i,out spec) || spec.MagickaCost>magicka)continue;
                    DaggerfallEntityBehaviour target;
                    EntityEffectManager effects;
                    if(!TryTarget(spec,body,manager,out target,out effects) || !Needed(spec,target,effects))continue;
                    result.Add(spec);
                }
                return result.ToArray();
            }
            catch{return new NpcCompanionCastSpec[0];}
        }

        // A bounded live read for one conversation snapshot. Unlike Discover(), it retains
        // exact affordable known spells even when no current recipient needs one.
        public static bool TryDiscoverKnownAffordable(GameObject actor,WorldStateStore store,
            out NpcCompanionCastSpec[] knownAffordable,out NpcCompanionCastSpec[] currentCandidates)
        {
            knownAffordable=new NpcCompanionCastSpec[0];currentCandidates=new NpcCompanionCastSpec[0];
            try
            {
                DaggerfallEntityBehaviour body;EntityEffectManager manager;NativeNpcVitalsBinding binding;
                ResidentActor resident;
                if(!TryActor(actor,store,out body,out manager,out binding,out resident) || manager.HasReadySpell)return false;
                int health,maxHealth,magicka,maxMagicka;
                if(!CurrentVitals(store,body,binding,resident,out health,out maxHealth,out magicka,out maxMagicka))return false;
                int count=body.Entity.SpellbookCount();
                if(count<0 || count>MaxSpells)return false;
                var known=new List<NpcCompanionCastSpec>();var candidates=new List<NpcCompanionCastSpec>();
                for(int i=0;i<count;i++)
                {
                    EffectBundleSettings settings;NpcCompanionCastSpec spell;
                    if(!body.Entity.GetSpell(i,out settings) || !Describe(body,manager,resident.Resident.Id,settings,i,out spell) ||
                        spell.MagickaCost>magicka)continue;
                    known.Add(spell);
                    DaggerfallEntityBehaviour target;EntityEffectManager effects;
                    if(TryTarget(spell,body,manager,out target,out effects) && Needed(spell,target,effects))candidates.Add(spell);
                }
                knownAffordable=known.ToArray();currentCandidates=candidates.ToArray();return true;
            }
            catch{return false;}
        }

        public static bool TryExecute(GameObject actor,WorldStateStore store,NpcCompanionCastSpec requested,
            Func<bool> dutyAvailable,out string reason)
        {
            reason="The known spell or its recipient is no longer available.";
            try
            {
                if(requested==null || dutyAvailable==null)return false;
                NpcCompanionCastSpec.CopyValidated(requested);
                DaggerfallEntityBehaviour body;EntityEffectManager manager;NativeNpcVitalsBinding binding;
                ResidentActor resident;
                if(!TryActor(actor,store,out body,out manager,out binding,out resident) || manager.HasReadySpell)return false;
                int health,maxHealth,magicka,maxMagicka;
                if(!CurrentVitals(store,body,binding,resident,out health,out maxHealth,out magicka,out maxMagicka))return false;
                int count=body.Entity.SpellbookCount();
                if(count<0 || count>MaxSpells)return false;
                EffectBundleSettings selected=new EffectBundleSettings();
                int matches=0,selectedIndex=-1;
                for(int i=0;i<count;i++)
                {
                    EffectBundleSettings settings;NpcCompanionCastSpec known;
                    if(body.Entity.GetSpell(i,out settings) && Describe(body,manager,resident.Resident.Id,settings,i,out known) &&
                        NpcCompanionCastSpec.Same(known,requested)){selected=settings;selectedIndex=i;matches++;}
                }
                if(matches!=1 || requested.MagickaCost>magicka)return false;
                DaggerfallEntityBehaviour target;EntityEffectManager effects;
                if(!TryTarget(requested,body,manager,out target,out effects) || !Needed(requested,target,effects))return false;
                int before=EffectValue(requested,target,effects);
                if(before<0 || !dutyAvailable() || !TryActor(actor,store,out DaggerfallEntityBehaviour currentBody,
                    out EntityEffectManager currentManager,out NativeNpcVitalsBinding currentBinding,out ResidentActor currentResident) ||
                    !ReferenceEquals(body,currentBody) || !ReferenceEquals(manager,currentManager) ||
                    !ReferenceEquals(binding,currentBinding) || !ReferenceEquals(resident,currentResident) || manager.HasReadySpell ||
                    !CurrentVitals(store,body,binding,resident,out int h,out int mh,out int m,out int mm) ||
                    h!=health || mh!=maxHealth || m!=magicka || mm!=maxMagicka ||
                    !TryTarget(requested,body,manager,out DaggerfallEntityBehaviour currentTarget,out EntityEffectManager currentEffects) ||
                    !ReferenceEquals(target,currentTarget) || !ReferenceEquals(effects,currentEffects) ||
                    EffectValue(requested,target,effects)!=before || selectedIndex<0 ||
                    !body.Entity.GetSpell(selectedIndex,out EffectBundleSettings stillSelected) ||
                    !Describe(body,manager,resident.Resident.Id,stillSelected,selectedIndex,out NpcCompanionCastSpec stillKnown) ||
                    !NpcCompanionCastSpec.Same(stillKnown,requested))return false;
                if(requested.Delivery==NpcCompanionCastDelivery.PlayerTouch && !TouchesPlayer(body,target))
                {reason="The player is outside unobstructed touch range.";return false;}

                var request=new NpcMagicRequest {ActorId=resident.Resident.Id,TargetId=requested.TargetId,
                    SpellId=requested.SpellId,EffectId=requested.EffectId,EffectKind=requested.EffectKind,
                    Intent=Intent(requested.EffectKind),MagickaCost=requested.MagickaCost,Hostile=false};
                NpcMagicAuthorization authorization;
                if(!NpcMagicActionContract.TryAuthorize(request,new NpcMagicAdmissionFacts {
                    KnownEffects=new[]{new NpcMagicKnownEffect {SpellId=requested.SpellId,EffectId=requested.EffectId,
                        EffectKind=requested.EffectKind,MagickaCost=requested.MagickaCost,Hostile=false}},
                    CurrentMagicka=magicka,TargetIsLegal=true,TargetIsAlive=target.Entity.CurrentHealth>0,
                    TargetConsents=requested.Delivery==NpcCompanionCastDelivery.PlayerTouch,
                    NativeAdapterAvailable=true,DutyAvailable=true,QuestProtected=false
                },out authorization))return false;

                var bundle=new EntityEffectBundle(selected,body);
                bool started=false;
                try
                {
                    if(!manager.SetReadySpell(bundle))return false;
                    started=true;
                    if(requested.Delivery==NpcCompanionCastDelivery.Self)manager.CastNoAnimSpell();
                    else
                    {
                        // The native touch ray has already selected this exact player body.
                        // AssignBundle runs native absorption, reflection, resistance, chance and MagicRound.
                        body.Entity.DecreaseMagicka(requested.MagickaCost);
                        effects.AssignBundle(bundle);
                        manager.AbortReadySpell();
                    }
                    binding.Capture();
                    int savedHealth,savedMaximumHealth,savedMagicka,savedMaximumMagicka;
                    if(!CurrentVitals(store,body,binding,resident,out savedHealth,out savedMaximumHealth,
                        out savedMagicka,out savedMaximumMagicka))
                    {reason="The cast occurred, but its resource change could not be saved.";return false;}
                    var outcome=new NpcMagicObservedOutcome {ActorId=request.ActorId,TargetId=request.TargetId,
                        SpellId=request.SpellId,EffectId=request.EffectId,EffectKind=request.EffectKind,
                        NativeAdapterExecuted=true,MagickaBefore=magicka,MagickaAfter=body.Entity.CurrentMagicka,
                        EffectValueBefore=before,EffectValueAfter=EffectValue(requested,target,effects)};
                    if(!NpcMagicActionContract.MatchesObservedOutcome(authorization,outcome))
                    {reason="The cast spent magicka but produced no verified effect.";return false;}
                    var effect=GameManager.Instance.EntityEffectBroker.GetEffectTemplate(selected.Effects[0].Key);
                    if(effect!=null)
                    {
                        int skill=(int)effect.Properties.MagicSkill;
                        var learnedClock=DaggerfallWorkshop.DaggerfallUnity.Instance.WorldTime.Now;
                        double learnedAt=learnedClock.ToSeconds()+(learnedClock.Second-Math.Floor(learnedClock.Second));
                        int skillGain;
                        if(NativeNpcCompanionExperience.TryAwardSuccessfulUse(store,resident.Resident.Id,skill,
                            learnedAt,out skillGain) && skillGain>0)
                            body.Entity.Skills.SetPermanentSkillValue(skill,(short)Math.Min(100,body.Entity.Skills.GetPermanentSkillValue(skill)+skillGain));
                    }
                    reason=null;return true;
                }
                finally
                {if(started && manager && ReferenceEquals(manager.ReadySpell,bundle))manager.AbortReadySpell();}
            }
            catch{return false;}
        }

        private static bool TryActor(GameObject actor,WorldStateStore store,out DaggerfallEntityBehaviour body,
            out EntityEffectManager manager,out NativeNpcVitalsBinding binding,out ResidentActor resident)
        {
            body=null;manager=null;binding=null;resident=null;
            if(!actor || !actor.activeInHierarchy || store==null)return false;
            body=actor.GetComponent<DaggerfallEntityBehaviour>();manager=actor.GetComponent<EntityEffectManager>();
            binding=actor.GetComponent<NativeNpcVitalsBinding>();resident=actor.GetComponent<ResidentActor>();
            return body && body.Entity!=null && body.Entity.CurrentHealth>0 && manager &&
                ReferenceEquals(manager.EntityBehaviour,body) && !manager.IsPlayerEntity && binding && resident &&
                resident.Resident!=null && NpcVitalsBindingPolicy.Eligible(resident.Resident.IsAlive,
                    resident.Resident.AgeGroup==ResidentAgeGroup.Adult,resident.Resident.IsQuestProtected);
        }

        private static bool CurrentVitals(WorldStateStore store,DaggerfallEntityBehaviour body,
            NativeNpcVitalsBinding binding,ResidentActor resident,out int health,out int maximumHealth,
            out int magicka,out int maximumMagicka)
        {
            health=maximumHealth=magicka=maximumMagicka=0;
            if(!binding.TryReadCurrent(out health,out maximumHealth,out magicka,out maximumMagicka) ||
                health!=body.Entity.CurrentHealth || maximumHealth!=body.Entity.MaxHealth ||
                magicka!=body.Entity.CurrentMagicka || maximumMagicka!=body.Entity.MaxMagicka)return false;
            var archetype=store.ResolveNpcArchetype(resident.Resident.Id);
            var saved=store.ReadNpcVitals(resident.Resident.Id,archetype);
            // An untouched body legitimately uses its full seeded baseline.
            // Matching actual native resources is sufficient; the first cast
            // creates the saved change instead of requiring a prior fake injury.
            return saved!=null && saved.IsAlive && saved.CurrentHealth==health &&
                saved.MaximumHealth==maximumHealth && saved.CurrentMagicka==magicka &&
                saved.MaximumMagicka==maximumMagicka;
        }

        private static bool Describe(DaggerfallEntityBehaviour body,EntityEffectManager manager,string actorId,
            EffectBundleSettings settings,int index,out NpcCompanionCastSpec spec)
        {
            spec=null;
            if(settings.BundleType!=BundleTypes.Spell ||
                settings.TargetType!=TargetTypes.CasterOnly && settings.TargetType!=TargetTypes.ByTouch ||
                settings.Effects==null || settings.Effects.Length!=1 || string.IsNullOrEmpty(settings.Effects[0].Key))return false;
            string key=settings.Effects[0].Key;
            NpcMagicEffectKind kind;
            if(key==HealHealth.EffectKey)kind=NpcMagicEffectKind.Health;
            else if(key==CurePoison.EffectKey || key==CureDisease.EffectKey)kind=NpcMagicEffectKind.Condition;
            else if(key==Shield.EffectKey)kind=NpcMagicEffectKind.TimedEffect;
            else return false;
            var effect=GameManager.Instance.EntityEffectBroker.InstantiateEffect(settings.Effects[0]);
            if(effect==null || effect.Key!=key ||
                kind==NpcMagicEffectKind.Health && effect.GetType()!=typeof(HealHealth) ||
                kind==NpcMagicEffectKind.Condition && effect.GetType()!=typeof(CurePoison) && effect.GetType()!=typeof(CureDisease) ||
                kind==NpcMagicEffectKind.TimedEffect && (effect.GetType()!=typeof(Shield) || !effect.Properties.SupportDuration))return false;
            DaggerfallEntity caster=manager.UsePlayerCharacterSkillsForEnemyMagicCost?null:body.Entity;
            (int _,int cost)=FormulaHelper.CalculateTotalEffectCosts(settings.Effects,settings.TargetType,caster,settings.MinimumCastingCost);
            if(cost<=0 || cost>body.Entity.MaxMagicka)return false;
            string payload=SaveLoadManager.Serialize(typeof(EffectBundleSettings),settings,false);
            if(string.IsNullOrEmpty(payload))return false;
            byte[] digest;
            using(var hash=SHA256.Create())digest=hash.ComputeHash(Encoding.UTF8.GetBytes(payload));
            spec=new NpcCompanionCastSpec {SpellId="native:spellbook:"+index+":"+BitConverter.ToString(digest).Replace("-","").ToLowerInvariant(),
                EffectId=key,EffectKind=kind,TargetId=settings.TargetType==TargetTypes.CasterOnly?actorId:"player",
                Delivery=settings.TargetType==TargetTypes.CasterOnly?NpcCompanionCastDelivery.Self:NpcCompanionCastDelivery.PlayerTouch,
                MagickaCost=cost};
            return true;
        }

        private static bool TryTarget(NpcCompanionCastSpec spec,DaggerfallEntityBehaviour body,EntityEffectManager manager,
            out DaggerfallEntityBehaviour target,out EntityEffectManager effects)
        {
            target=spec.Delivery==NpcCompanionCastDelivery.Self?body:GameManager.Instance.PlayerEntityBehaviour;
            effects=spec.Delivery==NpcCompanionCastDelivery.Self?manager:GameManager.Instance.PlayerEffectManager;
            return target && target.Entity!=null && target.Entity.CurrentHealth>0 && effects &&
                ReferenceEquals(effects.EntityBehaviour,target) &&
                (spec.Delivery!=NpcCompanionCastDelivery.PlayerTouch || spec.TargetId=="player");
        }

        private static bool Needed(NpcCompanionCastSpec spec,DaggerfallEntityBehaviour target,EntityEffectManager manager)
        {
            if(spec.EffectKind==NpcMagicEffectKind.Health)return target.Entity.CurrentHealth<target.Entity.MaxHealth;
            if(spec.EffectKind==NpcMagicEffectKind.Condition)return EffectValue(spec,target,manager)>0;
            return spec.EffectKind==NpcMagicEffectKind.TimedEffect && EffectValue(spec,target,manager)==0;
        }

        private static int EffectValue(NpcCompanionCastSpec spec,DaggerfallEntityBehaviour target,EntityEffectManager manager)
        {
            if(spec.EffectKind==NpcMagicEffectKind.Health)return target.Entity.CurrentHealth;
            if(spec.EffectKind==NpcMagicEffectKind.Condition)
                return spec.EffectId==CurePoison.EffectKey?manager.PoisonCount:
                    spec.EffectId==CureDisease.EffectKey?manager.DiseaseCount:-1;
            if(spec.EffectKind!=NpcMagicEffectKind.TimedEffect || spec.EffectId!=Shield.EffectKey)return -1;
            return NativeNpcShieldState.ProtectionRounds(manager);
        }

        private static bool TouchesPlayer(DaggerfallEntityBehaviour caster,DaggerfallEntityBehaviour player)
        {
            var collider=player.GetComponent<Collider>();
            if(!collider)return false;
            Vector3 origin=caster.transform.position;
            Vector3 direction=collider.bounds.center-origin;
            if(direction.sqrMagnitude<0.0001f)return false;
            return ReferenceEquals(DaggerfallMissile.GetEntityTargetInTouchRange(origin,direction.normalized),player);
        }

        private static NpcMagicIntent Intent(NpcMagicEffectKind kind)
        {return kind==NpcMagicEffectKind.Health?NpcMagicIntent.Heal:
            kind==NpcMagicEffectKind.Condition?NpcMagicIntent.Cure:NpcMagicIntent.Buff;}
    }
}
