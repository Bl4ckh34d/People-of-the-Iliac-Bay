using System;
#if !NATIVE_HAUNTING_TARGET_TEST
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Questing;
using UnityEngine;
#endif

namespace PeopleOfTheIliacBay.DFU
{
    internal static class NativeHauntingTargetPolicy
    {
        internal static bool InScope(string questName, bool hauntingMobile, bool liftedCurse, double now)
        {
            if (!hauntingMobile || liftedCurse || double.IsNaN(now) || double.IsInfinity(now) || now < 0 ||
                !string.Equals(questName, "S0000977", StringComparison.OrdinalIgnoreCase)) return false;
            double hour = now % 86400;
            return hour < 5 * 3600 || hour >= 19 * 3600;
        }
    }

#if !NATIVE_HAUNTING_TARGET_TEST
    // Native GetTargets excludes CivilianEntity and quest enemies normally select only
    // the player. Pin only this haunting's visible outdoor victim, as guard defense does.
    // Native senses, movement, attack timing and animation remain the combat owners.
    [DefaultExecutionOrder(-100)]
    internal sealed class NativeHauntingTargets : MonoBehaviour
    {
        private Func<bool> enabledBySettings;
        private DaggerfallEntityBehaviour body;
        private DaggerfallEnemy enemy;
        private EnemySenses senses;
        private EnemyMotor motor;
        private DaggerfallEntityBehaviour selected;
        private float nextSurvey;

        internal void Install(Func<bool> enabled)
        {
            enabledBySettings = enabled;
            body = GetComponent<DaggerfallEntityBehaviour>();
            enemy = GetComponent<DaggerfallEnemy>();
            senses = GetComponent<EnemySenses>();
            motor = GetComponent<EnemyMotor>();
        }

        private bool Available()
        {
            if (enabledBySettings == null || !enabledBySettings() || !body || body.Entity == null ||
                body.Entity.CurrentHealth <= 0 || !enemy || !senses || !motor || !motor.IsHostile ||
                !senses.enabled || !motor.enabled || GameManager.IsGamePaused || GameManager.Instance.DisableAI ||
                !gameObject.activeInHierarchy) return false;
            var resource = GetComponent<QuestResourceBehaviour>();
            var quest = resource ? QuestMachine.Instance.GetQuest(resource.QuestUID) : null;
            int mobile = enemy.MobileUnit.Enemy.ID;
            return NativeHauntingTargetPolicy.InScope(quest == null ? null : quest.QuestName,
                mobile == (int)MobileTypes.Ghost || mobile == (int)MobileTypes.Wraith,
                GameManager.Instance.PlayerEntity.GlobalVars.GetGlobalVar(0),
                DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
        }

        private bool VisibleVictim(DaggerfallEntityBehaviour candidate)
        {
            if (!candidate || candidate == body || !candidate.gameObject.activeInHierarchy ||
                candidate.Entity == null || candidate.Entity.CurrentHealth <= 0 ||
                candidate.Entity.IsMagicallyConcealed && !enemy.MobileUnit.Enemy.SeesThroughInvisibility)
                return false;
            var resident = candidate.GetComponent<ResidentActor>();
            var guard = candidate.GetComponent<GuardActor>();
            if (!resident && !guard) return false;
            if (resident && (resident.Resident == null || !resident.Resident.IsAlive || resident.Resident.IsQuestProtected)) return false;
            if (guard && guard.IsInterior) return false;
            var resource = candidate.GetComponent<QuestResourceBehaviour>();
            if (resource && !resource.IsAttackableByAI) return false;
            var source = DefenseScene.For(body);
            var destination = DefenseScene.For(candidate);
            if (source == null || destination == null || source.Interior || destination.Interior ||
                source.Root != destination.Root || !source.PlayerPresent) return false;
            if (!EnsureTargetController(candidate)) return false;
            return NativeSight(candidate);
        }

        private static bool EnsureTargetController(DaggerfallEntityBehaviour target)
        {
            if (target.GetComponent<CharacterController>()) return true;
            var capsule = target.GetComponent<CapsuleCollider>();
            if (!NativeNpcCombatRoleDecision.CanSupplyNativeTargetController(false, capsule != null)) return false;
            // CrowdMotion retains the live capsule; native AI only reads this geometry.
            var controller = target.gameObject.AddComponent<CharacterController>();
            controller.enabled = false;
            controller.center = capsule.center;
            controller.radius = capsule.radius;
            controller.height = capsule.height;
            return true;
        }

        private bool NativeSight(DaggerfallEntityBehaviour target)
        {
            // Match EnemySenses.CanSeeTarget's eye ray, range and field of view.
            // Its public delegate reads private direction/distance for the current
            // target, so it cannot safely survey an arbitrary new candidate.
            Vector3 delta = target.transform.position - transform.position;
            if (delta.magnitude >= senses.SightRadius + enemy.MobileUnit.Enemy.SightModifier ||
                Vector3.Angle(delta.normalized, transform.forward) >= senses.FieldOfView * .5f) return false;
            var source = GetComponent<CharacterController>();
            var destination = target.GetComponent<CharacterController>();
            if (!source || !destination) return false;
            Vector3 eye = transform.position + source.center + Vector3.up * (source.height / 3);
            Vector3 targetEye = target.transform.position + destination.center + Vector3.up * (destination.height / 3);
            RaycastHit hit;
            return Physics.Raycast(new Ray(eye, (targetEye - eye).normalized), out hit, senses.SightRadius) &&
                hit.transform.gameObject.GetComponent<DaggerfallEntityBehaviour>() == target;
        }

        private void FixedUpdate() { MaintainTarget(); }
        private void LateUpdate() { MaintainTarget(); }
        private void MaintainTarget()
        {
            if (!Available()) { ClearTarget(); return; }
            if (selected && !VisibleVictim(selected)) ClearTarget();
            if (Time.unscaledTime >= nextSurvey)
            {
                nextSurvey = Time.unscaledTime + .25f;
                float best = selected ? (selected.transform.position - transform.position).sqrMagnitude : float.PositiveInfinity;
                // Preserve a nearer player already seen by native senses.
                var current = senses.Target;
                if (!selected && current == GameManager.Instance.PlayerEntityBehaviour && senses.TargetInSight)
                    best = Mathf.Min(best, (current.transform.position - transform.position).sqrMagnitude);
                DaggerfallEntityBehaviour winner = selected;
                foreach (var collider in Physics.OverlapSphere(transform.position, senses.SightRadius))
                {
                    var candidate = collider.GetComponent<DaggerfallEntityBehaviour>();
                    if (!candidate) continue;
                    float distance = (candidate.transform.position - transform.position).sqrMagnitude;
                    if (distance >= best || !VisibleVictim(candidate)) continue;
                    best = distance; winner = candidate;
                }
                if (winner != selected)
                {
                    selected = winner;
                    motor.MakeEnemyHostileToAttacker(selected);
                }
            }
            if (selected) { senses.Target = selected; senses.SecondaryTarget = null; }
        }

        private void ClearTarget()
        {
            if (senses && selected && senses.Target == selected)
            {
                senses.Target = null;
                senses.SecondaryTarget = null;
                senses.DetectedTarget = false;
                senses.LastKnownTargetPos = EnemySenses.ResetPlayerPos;
                senses.PredictedTargetPos = EnemySenses.ResetPlayerPos;
                if (motor) motor.GiveUpTimer = 0;
            }
            selected = null;
        }

        private void OnDisable() { ClearTarget(); }
    }
#endif
}
