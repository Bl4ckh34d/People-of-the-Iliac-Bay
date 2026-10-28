using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.MagicAndEffects;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class GuardActor
    {
        private DaggerfallUnityItem guardBow,guardArrows,guardMelee;
        private float nextArrowAt;
        private bool wallNativeAttackReady;
        private Coroutine wallAttackStart;
        private void EnsureWallBowEquipped()
        {
            if(guardBow==null)InitializeArchery();
            if(guardBow!=null && entity.ItemEquipTable.GetItem(EquipSlots.RightHand)!=guardBow)
                entity.ItemEquipTable.EquipItem(guardBow,true,false);
        }
        private void InitializeArchery()
        {
            var record=roster.Guards.Find(g=>g.Id==GuardId);
            GuardArchery.EnsureLoadout(record);
            if(!record.HasBow) return;
            guardMelee=entity.ItemEquipTable.GetItem(EquipSlots.RightHand);
            guardBow=ItemBuilder.CreateWeapon(Weapons.Long_Bow,WeaponMaterialTypes.Steel);
            entity.Items.AddItem(guardBow);
            if(record.ArrowsRemaining>0)
            {
                guardArrows=ItemBuilder.CreateWeapon(Weapons.Arrow,WeaponMaterialTypes.None);
                guardArrows.stackCount=record.ArrowsRemaining;entity.Items.AddItem(guardArrows);
            }
        }
        private void TickGuardArchery()
        {
            var motor=GetComponent<EnemyMotor>();
            var senses=GetComponent<EnemySenses>();
            var target=senses.Target;
            var record=roster.Guards.Find(g=>g.Id==GuardId);
            if(!CombatReady || !target || target.Entity.CurrentHealth<=0 || entity.IsParalyzed) return;
            ShootGuardArrow(target,false);
        }
        public void DefendWallPost(DaggerfallEntityBehaviour target)
        {
            if(!target || target.Entity==null || target.Entity.CurrentHealth<=0 || entity.IsParalyzed)return;
            if(WallLawActive && target!=GameManager.Instance.PlayerEntityBehaviour)EndCombat();
            if(guardBow==null)InitializeArchery();
            if(!wallNativeAttackReady)
            {
                if(wallAttackStart==null)wallAttackStart=StartCoroutine(InitializeWallAttack());
                return;
            }
            // Let the native motor choose pursuit/retreat and reject walls and
            // cliffs. Place no longer resets a fighting archer to its duty post.
            var senses=GetComponent<EnemySenses>();
            var motor=GetComponent<EnemyMotor>();
            motor.MakeEnemyHostileToAttacker(target);motor.IsHostile=target==GameManager.Instance.PlayerEntityBehaviour;
            motor.enabled=true;senses.enabled=true;GetComponent<EnemyAttack>().enabled=true;
            senses.Target=target;senses.LastKnownTargetPos=target.transform.position;
            senses.OldLastKnownTargetPos=target.transform.position;senses.PredictedTargetPos=target.transform.position;
            WallCombatActive=true;wallCombatTarget=target;wallCombatDisplaced=true;wallReturnCorners=null;
            if(target==GameManager.Instance.PlayerEntityBehaviour &&
                GameManager.Instance.PlayerEntity.CrimeCommitted!=PlayerEntity.Crimes.None)
            {
                // Native damage and court keep their existing ownership.
                WallLawActive=true;
                if(OfferWallArrest(target))return;
            }
            ShootGuardArrow(target,true);
        }
        private System.Collections.IEnumerator InitializeWallAttack()
        {
            var attack=GetComponent<EnemyAttack>();
            attack.enabled=true;GetComponent<EnemyMotor>().enabled=true;GetComponent<EnemySenses>().enabled=true;
            yield return null; // Let the native Start cache its damage dependencies.
            attack.enabled=false;GetComponent<EnemyMotor>().enabled=false;GetComponent<EnemySenses>().enabled=false;
            wallNativeAttackReady=true;wallAttackStart=null;
        }
        private void ShootGuardArrow(DaggerfallEntityBehaviour target,bool holdPost)
        {
            var motor=GetComponent<EnemyMotor>();
            var record=roster.Guards.Find(g=>g.Id==GuardId);
            float distance=Vector3.Distance(transform.position,target.transform.position);
            bool canShoot=record.HasBow && record.ArrowsRemaining>0 && guardBow!=null && guardArrows!=null &&
                distance>(holdPost ? (WallLawActive ? GetComponent<EnemyAttack>().MeleeDistance : .1f) : EnemyAttack.minRangedDistance) && distance<EnemyAttack.maxRangedDistance &&
                ClearGuardArrowShot(target);
            if(!canShoot)
            {
                motor.enabled=true;
                if(guardMelee!=null && entity.ItemEquipTable.GetItem(EquipSlots.RightHand)==guardBow)
                    entity.ItemEquipTable.EquipItem(guardMelee,true,false);
                return;
            }
            // City-watch art has no native ranged attack frames. Use native arrows
            // and hit resolution without pretending unsupported frames exist.
            motor.enabled=true;
            if(Time.time<nextArrowAt) return;
            var attack=GetComponent<EnemyAttack>();
            if(!attack.ArrowMissilePrefab) return;
            entity.ItemEquipTable.EquipItem(guardBow,true,false);
            var missile=Instantiate(attack.ArrowMissilePrefab);
            missile.Caster=GetComponent<DaggerfallEntityBehaviour>();
            missile.TargetType=TargetTypes.SingleTargetAtRange;missile.ElementType=ElementTypes.None;missile.IsArrow=true;
            // Native missile aiming already supplies its one muzzle/model
            // height offset and current sensed target. Do not add it twice.
            record.ArrowsRemaining--;guardArrows.stackCount=record.ArrowsRemaining;
            if(record.ArrowsRemaining==0) entity.Items.RemoveItem(guardArrows);
            nextArrowAt=Time.time+2.5f;
        }
        private bool ClearGuardArrowShot(DaggerfallEntityBehaviour target)
        {
            var from=transform.position+Vector3.up*GetComponent<CharacterController>().height/3;
            var delta=target.transform.position-from;
            foreach(var hit in Physics.RaycastAll(from,delta.normalized,delta.magnitude,~0,QueryTriggerInteraction.Ignore))
            {
                if(hit.collider.transform.IsChildOf(transform)) continue;
                if(hit.collider.GetComponentInParent<DaggerfallEntityBehaviour>()==target) continue;
                return false;
            }
            return true;
        }
    }
}
