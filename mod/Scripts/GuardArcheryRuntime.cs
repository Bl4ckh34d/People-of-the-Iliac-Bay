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
            float distance=Vector3.Distance(transform.position,target.transform.position);
            bool canShoot=record.HasBow && record.ArrowsRemaining>0 && guardBow!=null && guardArrows!=null &&
                distance>EnemyAttack.minRangedDistance && distance<EnemyAttack.maxRangedDistance &&
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
            motor.enabled=false;
            // The disabled motor no longer updates Idle/Move from velocity.
            if(mobile.EnemyState!=MobileStates.Idle)mobile.ChangeEnemyState(MobileStates.Idle);
            var facing=target.transform.position-transform.position;facing.y=0;
            if(facing.sqrMagnitude>0.001f) transform.forward=facing.normalized;
            if(Time.time<nextArrowAt) return;
            var attack=GetComponent<EnemyAttack>();
            if(!attack.ArrowMissilePrefab) return;
            entity.ItemEquipTable.EquipItem(guardBow,true,false);
            var missile=Instantiate(attack.ArrowMissilePrefab);
            missile.Caster=GetComponent<DaggerfallEntityBehaviour>();
            missile.TargetType=TargetTypes.SingleTargetAtRange;missile.ElementType=ElementTypes.None;missile.IsArrow=true;
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
