using System;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Utility;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static class NativeNpcDiseases
    {
        internal static bool Eligible(DaggerfallEntityBehaviour host)
        {
            var actor=host?host.GetComponent<ResidentActor>():null;
            return actor && actor.Resident!=null && actor.Resident.IsAlive && !actor.Resident.IsQuestProtected &&
                actor.Resident.AgeGroup==ResidentAgeGroup.Adult && host.Entity!=null && host.Entity.CurrentHealth>0;
        }

        internal static void Install(Mod mod)
        {
            var broker=GameManager.Instance.EntityEffectBroker;
            for(int i=0;i<=16;i++)
            {
                var original=broker.GetEffectTemplate(DiseaseEffect.GetClassicDiseaseEffectKey((Diseases)i));
                // Custom disease replacements keep their own rules. Vampirism/lycanthropy are separate effects.
                if(original==null || original.GetType().BaseType!=typeof(DiseaseEffect))continue;
                var type=typeof(ResidentDisease<>).MakeGenericType(original.GetType());
                broker.RegisterEffectTemplate((BaseEntityEffect)Activator.CreateInstance(type),true);
            }
            FormulaHelper.RegisterOverride<Action<DaggerfallEntity,DaggerfallEntity,Diseases[]>>(
                mod,"InflictDisease",InflictDisease);
        }

        private static void InflictDisease(DaggerfallEntity attacker,DaggerfallEntity target,Diseases[] diseases)
        {
            // The native formula restricts this to players; extend only the target gate to our residents.
            if(target==null || diseases==null || diseases.Length==0 || !target.EntityBehaviour)return;
            bool player=ReferenceEquals(target,GameManager.Instance.PlayerEntity);
            if(!player && !Eligible(target.EntityBehaviour) || player && target.Level==1)return;
            if(FormulaHelper.SavingThrow(DFCareer.Elements.DiseaseOrPoison,DFCareer.EffectFlags.Disease,target,0)==0)return;
            var effects=target.EntityBehaviour.GetComponent<EntityEffectManager>();if(!effects)return;
            var disease=diseases[UnityEngine.Random.Range(0,diseases.Length)];
            if((int)disease<0 || (int)disease>16)return;
            effects.AssignBundle(effects.CreateDisease(disease),AssignBundleFlags.BypassSavingThrows);
        }
    }

    // DFU's classic disease data, damage, curing and save format; only its player-only start is adapted.
    public sealed class ResidentDisease<T> : DiseaseEffect where T:DiseaseEffect,new()
    {
        public override void SetProperties()
        {
            var native=new T();properties=native.Properties;classicDiseaseType=native.ClassicDiseaseType;
            diseaseData=GetClassicDiseaseData(classicDiseaseType);
        }
        public override TextFile.Token[] ContractedMessageTokens {get{return GetClassicContractedMessageTokens(classicDiseaseType);}}

        public override void Start(EntityEffectManager owner,DaggerfallEntityBehaviour source=null)
        {
            var host=owner?owner.EntityBehaviour:null;
            if(!NativeNpcDiseases.Eligible(host)) {base.Start(owner,source);return;}
            foreach(var bundle in owner.EffectBundles)
                foreach(var effect in bundle.liveEffects)
                    if(effect.Key==Key && effect is IncumbentEffect && ((IncumbentEffect)effect).IsIncumbent && !effect.HasEnded)
                    {EndDisease();return;}
            lastDay=DaggerfallUnity.Instance.WorldTime.DaggerfallDateTime.ToClassicDaggerfallTime()/DaggerfallDateTime.MinutesPerDay;
            if(!IsDiseasePermanent())daysOfSymptomsLeft=UnityEngine.Random.Range(diseaseData.daysOfSymptomsMin,diseaseData.daysOfSymptomsMax+1);
            // Resume initializes the public native incumbent lifecycle without the player-only Start gate.
            base.Resume(new EntityEffectManager.EffectSaveData_v1 {key=Key,effectSettings=Settings,roundsRemaining=1,
                chanceSuccess=true,isIncumbent=true,variantCount=1,statMods=new int[DaggerfallStats.Count],
                statMaxMods=new int[DaggerfallStats.Count],skillMods=new int[DaggerfallSkills.Count]},owner,source);
        }

        protected override void UpdateDisease()
        {
            if(manager && manager.IsPlayerEntity) {base.UpdateDisease();return;}
            if(forcedRoundsRemaining==0 || daysOfSymptomsLeft==completedDiseaseValue)return;
            uint day=DaggerfallUnity.Instance.WorldTime.DaggerfallDateTime.ToClassicDaggerfallTime()/DaggerfallDateTime.MinutesPerDay;
            if(day<=lastDay)return;
            int elapsed=(int)(day-lastDay);incubationOver=true;
            for(int i=0;i<elapsed;i++)IncrementDailyDiseaseEffects();
            lastDay=day;
            if(!IsDiseasePermanent() && (daysOfSymptomsLeft-=elapsed)<=0) {daysOfSymptomsLeft=0;EndDisease();}
            // NPC symptoms must not display the player's "you feel bad" HUD message.
        }
    }
}
