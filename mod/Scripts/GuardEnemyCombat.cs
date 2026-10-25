using System.Collections;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class GuardActor
    {
        public DaggerfallEntityBehaviour DefenseTarget { get; private set; }
        private EnemySenses.CanSeeTargetCallback originalDefenseSight;
        private EnemySenses.CanHearTargetCallback originalDefenseHearing;
        private EnemySenses.CanDetectOtherwiseCallback originalDefenseDetection;
        public void BeginDefenseCombat(DaggerfallEntityBehaviour target, double now)
        {
            if (!target || CombatReady || combatStart != null) return;
            if (!EnsureNativeTargetController(target)) return;
            var guard = roster.Guards.Find(g => g.Id == GuardId);
            if (guard.Duty != GuardDuty.Engaged) roster.Engage(GuardId,now);
            DefenseTarget = target;
            CaptureDefense(now);
            combatStart = StartCoroutine(StartDefenseCombat());
        }
        private static bool EnsureNativeTargetController(DaggerfallEntityBehaviour target)
        {
            if (!target) return false;
            var controller = target.GetComponent<CharacterController>();
            if (controller) return true;
            var capsule = target.GetComponent<CapsuleCollider>();
            if (!NativeNpcCombatRoleDecision.CanSupplyNativeTargetController(false, capsule != null)) return false;
            // CrowdMotion owns this resident's actual movement/collision. Native
            // guard AI only reads the target controller's geometry, so preserve
            // capsule ownership with a disabled matching controller.
            controller = target.gameObject.AddComponent<CharacterController>();
            controller.center = capsule.center;
            controller.radius = capsule.radius;
            controller.height = capsule.height;
            controller.enabled = false;
            return true;
        }
        private IEnumerator StartDefenseCombat()
        {
            var motor = GetComponent<EnemyMotor>(); var senses = GetComponent<EnemySenses>();
            motor.enabled = true; senses.enabled = true;
            yield return null;
            if (!DefenseTarget) { combatStart = null; yield break; }
            originalDefenseSight = senses.CanSeeTargetHandler;
            originalDefenseHearing = senses.CanHearTargetHandler;
            originalDefenseDetection = senses.CanDetectOtherwiseHandler;
            senses.CanSeeTargetHandler = candidate => candidate == DefenseTarget && originalDefenseSight(candidate);
            senses.CanHearTargetHandler = () => false;
            senses.CanDetectOtherwiseHandler = candidate => false;
            // IsHostile is specifically hostility to the player. Keep it false for enemy defense.
            motor.IsHostile = false;
            motor.MakeEnemyHostileToAttacker(DefenseTarget);
            senses.Target = DefenseTarget; senses.SecondaryTarget = null;
            var guard = roster.Guards.Find(g => g.Id == GuardId);
            var last = IsInterior ? Scene(guard.Interior.LastSeenSuspect) :
                geometry.ScenePoint(new RoutePoint(guard.LastSeenSuspect.X,guard.LastSeenSuspect.Z)) + Vector3.up;
            senses.OldLastKnownTargetPos = last; senses.LastKnownTargetPos = last; senses.PredictedTargetPos = last;
            SayHaltOnce(DefenseTarget);
            GetComponent<EnemyAttack>().enabled = true;
            CombatReady = true; combatStart = null;
        }
        private void LateUpdate()
        {
            if(CombatReady && !GameManager.IsGamePaused) TickGuardArchery();
            if (!DefenseTarget || !CombatReady || GameManager.IsGamePaused) return;
            var motor = GetComponent<EnemyMotor>(); var senses = GetComponent<EnemySenses>();
            // A real player assault takes priority; the controller transfers back to native arrest.
            if (motor.IsHostile) return;
            // Native GetTargets omits enemy-vs-enemy targets when infighting is disabled.
            // Pin only the reported hostile enemy; native senses/motor/attack still perform combat.
            motor.IsHostile = false; senses.Target = DefenseTarget; senses.SecondaryTarget = null;
        }
        private void CaptureDefense(double now)
        {
            var guard = roster.Guards.Find(g => g.Id == GuardId);
            if (guard.Duty != GuardDuty.Engaged) return;
            var local = transform.parent.InverseTransformPoint(transform.position);
            bool sees = DefenseTarget && DefenseTarget.Entity.CurrentHealth > 0 && !DefenseTarget.Entity.IsInvisible &&
                CivilianResponses.Sees(transform.position,DefenseTarget.transform.position,20);
            var seen = sees ? transform.parent.InverseTransformPoint(DefenseTarget.transform.position) : Vector3.zero;
            if (IsInterior)
            {
                if (guard.Interior == null || guard.Interior.Returning) return;
                roster.ObserveInterior(GuardId,Point(local),(double)entity.CurrentHealth/entity.MaxHealth,
                    now,sees,sees ? Point(seen) : null);
                // Initial combat is admitted on a visible target; retain its interior
                // position separately from exterior report coordinates.
                return;
            }
            roster.ObserveEngagement(GuardId,new SavedRoutePoint { X = local.x, Z = local.z },
                (double)entity.CurrentHealth/entity.MaxHealth,now,sees,sees ? new SavedRoutePoint { X = seen.x, Z = seen.z } : null);
        }
        private void ClearDefenseTarget()
        {
            var senses = GetComponent<EnemySenses>();
            if (originalDefenseSight != null)
            {
                senses.CanSeeTargetHandler = originalDefenseSight;
                senses.CanHearTargetHandler = originalDefenseHearing;
                senses.CanDetectOtherwiseHandler = originalDefenseDetection;
            }
            originalDefenseSight = null; originalDefenseHearing = null; originalDefenseDetection = null; DefenseTarget = null;
        }
    }
}
