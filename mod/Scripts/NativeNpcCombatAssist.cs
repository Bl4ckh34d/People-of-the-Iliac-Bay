using System;
using System.Globalization;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Formulas;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Game.MagicAndEffects;
using DaggerfallWorkshop.Game.Questing;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>Uses the real resident weapon, skills and DFU hit formula. Owns no combat statistics.</summary>
    internal static class NativeNpcCombatAssist
    {
        internal const float MeleeRange = 1.65f;

        internal static string Identity(DaggerfallEntityBehaviour target)
        {
            if (!target || !(target.Entity is EnemyEntity) || target.GetComponent<GuardActor>() ||
                target.GetComponent<ResidentActor>() || target.GetComponent<StaticNPC>()) return null;
            var native = target.GetComponent<DaggerfallEnemy>();
            return native && native.LoadID != 0 ? "native:" + native.LoadID.ToString(CultureInfo.InvariantCulture) : null;
        }

        internal static DaggerfallEntityBehaviour Find(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            foreach (var body in UnityEngine.Object.FindObjectsOfType<DaggerfallEntityBehaviour>())
                if (Identity(body) == id) return body;
            return null;
        }

        internal static bool TryWeapon(ResidentActor actor, out DaggerfallUnityItem weapon)
        {
            weapon = null;
            var body = actor ? actor.GetComponent<DaggerfallEntityBehaviour>() : null;
            if (!body || body.Entity == null || body.Entity.CurrentHealth <= 0 || body.Entity.IsParalyzed) return false;
            var held = body.Entity.ItemEquipTable.GetItem(EquipSlots.RightHand);
            // DFU's unarmed formula has no CivilianEntity attack branch. Do not manufacture one.
            if (held == null || held.ItemGroup != ItemGroups.Weapons || held.currentCondition <= 0 || held.RepairData.IsBeingRepaired() ||
                held.GetWeaponSkillIDAsShort() == (short)DFCareer.Skills.Archery ||
                body.Entity.Items.GetItem(held.UID) != held ||
                body.Entity.Skills.GetLiveSkillValue(held.GetWeaponSkillIDAsShort()) <= 0) return false;
            weapon = held;
            return true;
        }

        internal static bool OwnershipClear(DaggerfallEntityBehaviour target)
        {
            if (!target || !(target.Entity is EnemyEntity) || target.GetComponent<QuestResourceBehaviour>()) return false;
            var native = target.GetComponent<DaggerfallEnemy>();
            return native && !native.QuestSpawn && target.Entity.Team != MobileTeams.PlayerAlly &&
                ((EnemyEntity)target.Entity).MobileEnemy.ID != (int)MobileTypes.Knight_CityWatch;
        }

        internal static bool ThreatensPlayerOrActor(DaggerfallEntityBehaviour target, ResidentActor actor, bool starting)
        {
            if (!target || target.Entity == null || target.Entity.CurrentHealth <= 0) return false;
            var motor = target.GetComponent<EnemyMotor>();
            var senses = target.GetComponent<EnemySenses>();
            var actorBody = actor ? actor.GetComponent<DaggerfallEntityBehaviour>() : null;
            return motor && motor.IsHostile && senses &&
                (senses.Target == GameManager.Instance.PlayerEntityBehaviour || !starting && senses.Target == actorBody);
        }

        internal static bool SameVisibleScene(ResidentActor actor, DaggerfallEntityBehaviour target, float range)
        {
            if (!actor || !target || !actor.gameObject.activeInHierarchy || !target.gameObject.activeInHierarchy ||
                target.Entity == null || target.Entity.IsInvisible) return false;
            var source = DefenseScene.For(actor);
            var destination = DefenseScene.For(target);
            return source != null && destination != null && source.Root == destination.Root && source.PlayerPresent &&
                CivilianResponses.Sees(actor.transform.position, target.transform.position, range);
        }

        internal static bool Unprovoked(WorldStateStore store, string id, int settlementId)
        {
            var record = store == null ? null : store.GetDefenseCombatant(id);
            return record != null && record.SettlementId == settlementId && !record.PlayerProvoked &&
                record.Origin == CombatOrigin.HostileAttacker && record.UnprovokedAttackAt >= 0;
        }

        internal static bool TrySwing(ResidentActor actor, DaggerfallEntityBehaviour target,WorldStateStore store,double now,
            Func<bool> stillAuthorized, out int damage)
        {
            damage = 0;
            DaggerfallUnityItem weapon;
            if (!actor || actor.IsSleeping || actor.IsSeated || stillAuthorized == null || !stillAuthorized() || !TryWeapon(actor, out weapon) ||
                !OwnershipClear(target) || !SameVisibleScene(actor, target, MeleeRange)) return false;
            var body = actor.GetComponent<DaggerfallEntityBehaviour>();
            if (!stillAuthorized()) return false;
            damage = FormulaHelper.CalculateAttackDamage(body.Entity, target.Entity, false, 0, weapon);
            var effects = body.GetComponent<EntityEffectManager>();
            if (weapon.IsEnchanted && effects)
                damage = effects.DoItemEnchantmentPayloads(EnchantmentPayloadFlags.Strikes, weapon,
                    body.Entity.Items, target, damage);
            damage = Math.Max(0, damage);
            var sounds = target.GetComponent<EnemySounds>();
            if (damage > 0)
            {
                if (sounds) sounds.PlayHitSound(weapon);
                int before=target.Entity.CurrentHealth;
                target.DamageHealthFromSource(body, damage, true, target.transform.position);
                int skill=weapon.GetWeaponSkillIDAsShort();
                int skillGain;
                if(target && target.Entity!=null && target.Entity.CurrentHealth<before &&
                    NativeNpcCompanionExperience.TryAwardSuccessfulUse(store,actor.Resident.Id,skill,now,out skillGain) && skillGain>0)
                    body.Entity.Skills.SetPermanentSkillValue(skill,(short)Math.Min(100,body.Entity.Skills.GetPermanentSkillValue(skill)+skillGain));
            }
            else if (sounds) sounds.PlayMissSound(weapon);
            var motor = target.GetComponent<EnemyMotor>();
            if (motor && target.Entity.CurrentHealth > 0) motor.MakeEnemyHostileToAttacker(body);
            // Native formula can consume poison and damage equipment even on an ineffective swing.
            actor.CaptureInventory();
            return true;
        }
    }
}
