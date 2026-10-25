using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.MagicAndEffects.MagicEffects;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    internal static class NpcMurderNativeProbe
    {
        internal static DaggerfallEntityBehaviour Spawn(BuildingDirectory directory,Vector3 position)
        {
            var body=GameObjectHelper.CreateFoeGameObjects(position,MobileTypes.Thief,1,MobileReactions.Passive)[0];
            body.transform.SetParent(directory.transform,true);body.SetActive(true);
            var entity=body.GetComponent<DaggerfallEntityBehaviour>();
            Require(body.GetComponent<DaggerfallEnemy>().LoadID!=0,"Native murderer lacks stable LoadID.");
            Quiesce(entity);entity.GetComponent<EnemyAttack>().enabled=true;return entity;
        }
        internal static void Attack(DaggerfallEntityBehaviour killer,ResidentActor victim,bool armed)
        {
            Quiesce(killer);killer.Entity.Level=10;
            killer.Entity.Skills.SetPermanentSkillValue(DaggerfallConnect.DFCareer.Skills.HandToHand,100);
            killer.Entity.Skills.SetPermanentSkillValue(DaggerfallConnect.DFCareer.Skills.LongBlade,100);
            killer.Entity.MaxHealth=20;killer.Entity.SetHealth(20,true);
            var senses=killer.GetComponent<EnemySenses>();senses.Target=victim.GetComponent<DaggerfallEntityBehaviour>();
            var weapon=armed?ItemBuilder.CreateWeapon(Weapons.Longsword,WeaponMaterialTypes.Iron):null;
            if(weapon!=null)killer.Entity.Items.AddItem(weapon);
            var effects=killer.GetComponent<EntityEffectManager>();
            var concealment=effects.CreateSpellBundle(InvisibilityNormal.EffectKey,ElementTypes.Magic,
                new EffectSettings{DurationBase=2000,DurationPerLevel=1});
            effects.AssignBundle(concealment,AssignBundleFlags.BypassSavingThrows|AssignBundleFlags.BypassChance);
            var normal=effects.FindIncumbentEffect<InvisibilityNormal>();
            Require(normal!=null && !normal.HasEnded && killer.Entity.IsMagicallyConcealedNormalPower,
                "Native incoming-melee fixture did not start normal-power invisibility.");
            var hit=typeof(EnemyAttack).GetMethod("ApplyDamageToNonPlayer",BindingFlags.Instance|BindingFlags.NonPublic);
            for(int i=0;i<30 && victim.Resident.IsAlive;i++)
                hit.Invoke(killer.GetComponent<EnemyAttack>(),new object[]{weapon,victim.transform.position-killer.transform.position,false});
            Require(!victim.Resident.IsAlive,"Native NPC melee did not kill the controlled injured resident.");
            Require(normal.HasEnded && !killer.Entity.IsMagicallyConcealedNormalPower,
                "Native NPC hit on an actual civilian retained normal-power invisibility.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CIVILIAN_HIT_CONCEALMENT_PASS: normalEffectEnded=true; nativeMelee=true; actualCivilianEntity=true; armed="+armed);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_MURDER_ATTACK_PASS: nativeMelee=true; actualCivilianEntity=true; sourceIsNpc=true; playerUncharged=true; armed="+armed);
        }
        private static void Quiesce(DaggerfallEntityBehaviour body)
        {
            body.GetComponent<EnemyMotor>().enabled=false;body.GetComponent<EnemyMotor>().IsHostile=false;
            body.GetComponent<EnemySenses>().enabled=false;body.GetComponent<EnemyAttack>().enabled=false;
        }
        internal static void Conceal(DaggerfallEntityBehaviour body,bool hidden)
        {
            var manager=body.GetComponent<EntityEffectManager>();
            foreach(var bundle in manager.EffectBundles)
                if(bundle.name=="Native murderer probe concealment")manager.RemoveBundle(bundle);
            if(hidden)
            {
                var bundle=manager.CreateSpellBundle("Invisibility-True",ElementTypes.Magic,
                    new EffectSettings{DurationBase=2000,DurationPerLevel=1});
                var settings=bundle.Settings;settings.Name="Native murderer probe concealment";bundle.Settings=settings;
                manager.AssignBundle(bundle,AssignBundleFlags.BypassSavingThrows|AssignBundleFlags.BypassChance);
            }
            var mask=MagicalConcealmentFlags.InvisibleNormal|MagicalConcealmentFlags.InvisibleTrue;
            body.Entity.MagicalConcealmentFlags=hidden?body.Entity.MagicalConcealmentFlags|MagicalConcealmentFlags.InvisibleTrue:
                body.Entity.MagicalConcealmentFlags&~mask;
        }
        internal static IEnumerator Finish(GuardPopulation guards,WorldStateStore store,TownGeometry geometry,
            CivilianIncident alarm,GuardReport report,DaggerfallEntityBehaviour killer,GuardActor investigator,bool alreadyDead)
        {
            var game=GameManager.Instance;string identity=alarm.KillerThreatId;var record=store.GetDefenseCombatant(identity);
            Require(report.CorpseSuspect!=null && report.CorpseSuspect.Kind==CorpseSuspectKind.HostileEntity &&
                report.CorpseSuspect.Identity==identity && !alarm.PlayerAttributed && !alarm.AccusationIssued,
                "NPC murder testimony blamed player or lost exact native identity.");
            Require(record!=null && record.UnprovokedAttackAt>=0 && !record.PlayerProvoked,
                "Actual NPC attack lost unprovoked aggression provenance.");
            if(!alreadyDead)
            {
                Require(killer && killer.Entity.IsInvisible && report.SuspectSighting==null && !investigator.DefenseTarget,
                    "Hidden murderer was located or attacked before actual sight.");
                yield return new WaitForSecondsRealtime(1.2f);
                Require(report.SuspectSighting==null && !investigator.DefenseTarget,"Hidden murderer acquired through native concealment.");
                bool placed=false;
                foreach(var direction in new[]{Vector3.right,Vector3.left,Vector3.forward,Vector3.back})
                {
                    var point=investigator.transform.position+direction*6;
                    if(!CivilianResponses.Sees(investigator.transform.position,point,20))continue;
                    killer.transform.position=point;placed=true;break;
                }
                Require(placed,"No visible native murderer position near investigator.");Conceal(killer,false);
            }
            int warnings=investigator.HaltWarnings;bool fought=false;float deadline=Time.realtimeSinceStartup+120;
            while(Time.realtimeSinceStartup<deadline)
            {
                fought|=investigator.DefenseTarget && investigator.DefenseTarget==killer && investigator.CombatReady;
                if(investigator && investigator.gameObject.activeInHierarchy)
                    game.PlayerMotor.transform.position=investigator.transform.position+Vector3.back*6+Vector3.up;
                Require(game.PlayerEntity.CrimeCommitted==PlayerEntity.Crimes.None && !alarm.AccusationIssued,
                    "NPC murderer response accused innocent player.");
                if(record.Death!=null && record.Death.Cleanup!=null && record.Death.Cleanup.Cleared && alarm.CrimeResolved &&
                    guards.Roster.Guards.Find(g=>g.Id==investigator.GuardId).Duty==GuardDuty.Posted)break;
                yield return null;
            }
            Require(record.Death!=null && record.Death.CorpseId!=null && record.Death.Lawful && !record.Death.KilledByPlayer &&
                record.Death.Cleanup!=null && record.Death.Cleanup.Cleared && alarm.CrimeResolved,
                "NPC murderer death/cleanup unresolved: dead="+(record.Death!=null)+"; cleanup="+
                (record.Death!=null && record.Death.Cleanup!=null && record.Death.Cleanup.Cleared)+"; resolved="+alarm.CrimeResolved);
            Require(alreadyDead?!fought:fought && report.SuspectSighting!=null && investigator.HaltWarnings>warnings,
                "NPC murderer sight/Halt/native combat contract failed.");
            foreach(var loot in UnityEngine.Object.FindObjectsOfType<DaggerfallLoot>())
                Require(!loot || "loot:"+loot.LoadID!=record.Death.CorpseId,"Removed murderer corpse still rendered.");
            var guard=guards.Roster.Guards.Find(g=>g.Id==investigator.GuardId);
            Require(guard.Duty==GuardDuty.Posted && Vector3.Distance(investigator.transform.position,
                geometry.ScenePoint(new RoutePoint(guard.Post.X,guard.Post.Z))+Vector3.up)<1,
                "Investigator did not physically return to its post.");
            var wire=(WorldState)SaveLoadManager.Deserialize(typeof(WorldState),SaveLoadManager.Serialize(typeof(WorldState),store.Snapshot(),false));
            var restored=new WorldStateStore(0);restored.Restore(wire);
            Require(restored.GetDefenseCombatant(identity).Death.Cleanup.Cleared &&
                restored.OutdoorCorpses(alarm.SettlementId).Find(a=>a.Id==alarm.Id).CrimeResolved,"Native serializer lost NPC murder resolution.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_MURDER_NATIVE_PASS: alreadyDead="+alreadyDead+
                "; nativeAttack=true; witnessReported=true; victimRemoved=true; sightBeforeCombat=true; killerCorpseRemoved=true; postReturned=true; innocentPlayer=true; nativeSerializer=true");
        }
        private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    }
}
