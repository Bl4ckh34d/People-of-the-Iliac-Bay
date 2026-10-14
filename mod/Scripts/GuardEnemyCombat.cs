using System.Collections;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public sealed partial class GuardActor
    {
        public DaggerfallEntityBehaviour DefenseTarget { get; private set; }
        private EnemySenses.CanSeeTargetCallback originalDefenseSight;
        private EnemySenses.CanHearTargetCallback originalDefenseHearing;
        private EnemySenses.CanDetectOtherwiseCallback originalDefenseDetection;
        public void BeginDefenseCombat(DaggerfallEntityBehaviour target, double now)
        {
            if (!target || IsInterior || CombatReady || combatStart != null) return;
            var guard = roster.Guards.Find(g => g.Id == GuardId);
            if (guard.Duty != GuardDuty.Engaged) roster.Engage(GuardId,now);
            DefenseTarget = target;
            CaptureDefense(now);
            combatStart = StartCoroutine(StartDefenseCombat());
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
            var last = geometry.ScenePoint(new RoutePoint(guard.LastSeenSuspect.X,guard.LastSeenSuspect.Z)) + Vector3.up;
            senses.OldLastKnownTargetPos = last; senses.LastKnownTargetPos = last; senses.PredictedTargetPos = last;
            SayHaltOnce(DefenseTarget);
            GetComponent<EnemyAttack>().enabled = true;
            CombatReady = true; combatStart = null;
        }
        private void LateUpdate()
        {
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
            bool sees = DefenseTarget && DefenseTarget.Entity.CurrentHealth > 0 &&
                CivilianResponses.Sees(transform.position,DefenseTarget.transform.position,20);
            var seen = sees ? transform.parent.InverseTransformPoint(DefenseTarget.transform.position) : Vector3.zero;
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
