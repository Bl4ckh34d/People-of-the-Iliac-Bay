using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private sealed class CombatActionMotion
        {
            public string JobId;
            public object Epoch;
            public DaggerfallEntityBehaviour Target;
            public float BeganAt, NextSwingAt;
            public bool Retreating, SawExactTarget;
        }
        private readonly Dictionary<string, CombatActionMotion> combatActionMotions = new Dictionary<string, CombatActionMotion>();
        private float nextCombatCleanupAt;

        private void ResetNpcCombatActions() { combatActionMotions.Clear(); nextCombatCleanupAt = 0; }

        private void PruneEndedCombatActions()
        {
            if (worldStore == null || Time.unscaledTime < nextCombatCleanupAt) return;
            nextCombatCleanupAt = Time.unscaledTime + 1;
            List<string> ended = null;
            foreach (var pair in combatActionMotions)
            {
                var active = worldStore.ActiveNpcAction(pair.Key);
                if (active != null && active.Id == pair.Value.JobId && !NpcActionExecutor.IsTerminal(active) &&
                    worldStore.IsNpcVitalsWriteEpochCurrent(pair.Value.Epoch)) continue;
                if (ended == null) ended = new List<string>();
                ended.Add(pair.Key);
            }
            if (ended != null) foreach (string id in ended) combatActionMotions.Remove(id);
        }

        // Health still captures native vitals and observes recovery need. The admitted
        // fighter's role owns immediate combat or retreat until this exact order ends.
        private bool CombatActionOwnsInjuryResponse(string personId)
        {
            if (string.IsNullOrEmpty(personId)) return false;
            PruneEndedCombatActions();
            var active = worldStore == null ? null : worldStore.ActiveNpcAction(personId);
            if (active == null || !IsCombatAction(active.Kind) || NpcActionExecutor.IsTerminal(active))
            { combatActionMotions.Remove(personId); return false; }
            return active.Admission != null && active.Admission.Kind == NpcOperationAdmissionKind.Conversation &&
                active.Target != null && active.Target.Kind == NpcActionTargetKind.Person;
        }

        private static bool IsCombatAction(NpcActionKind kind)
        { return kind == NpcActionKind.Attack || kind == NpcActionKind.Defend; }

        private void OfferCombatActions(ResidentActor actor, string text, List<NpcActionOption> result, bool semantic)
        {
            if (!actor || worldStore == null || result == null || !CombatActorRoleAvailable(actor, true)) return;
            foreach (var body in UnityEngine.Object.FindObjectsOfType<DaggerfallEntityBehaviour>())
            {
                string id = NativeNpcCombatAssist.Identity(body);
                if (!CombatActorAvailable(actor, id) || !CombatTargetAvailable(actor, body, id, true)) continue;
                foreach (var kind in new[] { NpcActionKind.Attack, NpcActionKind.Defend })
                {
                    if (result.Count >= (semantic ? NpcActionCandidateCatalog.MaximumCandidates : 24)) return;
                    result.Add(new NpcActionOption { Id = result.Count + 1, Kind = kind,
                        Target = new NpcActionTarget { Kind = NpcActionTargetKind.Person, PersonId = id },
                        Label = (kind == NpcActionKind.Defend ? "defend the player against " : "fight ") +
                            body.Entity.Name + ", the exact visible native attacker (" + id + ")" });
                }
            }
        }

        private bool CombatActorAvailable(ResidentActor actor, string exactThreatId = null, bool requireWeapon = true)
        {
            if (!actor || actor.Resident == null || !actor.Resident.IsAlive || actor.Resident.IsQuestProtected ||
                QuestOwned(actor.Resident) || actor.IsSleeping || !CombatActorRoleAvailable(actor, requireWeapon)) return false;
            if (!IsAlarmed(actor.Resident.Id)) return true;
            if (string.IsNullOrEmpty(exactThreatId)) return false;
            var alarm = worldStore.ActiveAlarm(actor.Resident.Id, DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
            return alarm != null && alarm.ThreatId == exactThreatId;
        }

        // Used by conversation's outer alarm gate before it enumerates action targets.
        // It is deliberately narrower than general actor availability: only the one
        // visible threat recorded by this actor's current alarm can open Fight/Defend.
        private bool CombatAlarmAllowsCandidate(ResidentActor actor)
        {
            if (!actor || worldStore == null || !CombatActorRoleAvailable(actor, true) || !IsAlarmed(actor.Resident.Id))
                return false;
            var alarm = worldStore.ActiveAlarm(actor.Resident.Id, DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
            if (alarm == null || string.IsNullOrEmpty(alarm.ThreatId)) return false;
            var target = NativeNpcCombatAssist.Find(alarm.ThreatId);
            return CombatTargetAvailable(actor, target, alarm.ThreatId, true);
        }

        // Admission repeats the same identity and visibility boundary after the
        // model selected one concrete option; no other alarmed action is admitted.
        private bool CombatAlarmAllowsAdmission(ResidentActor actor, NpcActionOption option)
        {
            if (option == null || !IsCombatAction(option.Kind) || option.Target == null ||
                option.Target.Kind != NpcActionTargetKind.Person || string.IsNullOrEmpty(option.Target.PersonId) ||
                !CombatActorAvailable(actor, option.Target.PersonId)) return false;
            var target = NativeNpcCombatAssist.Find(option.Target.PersonId);
            return CombatTargetAvailable(actor, target, option.Target.PersonId, true);
        }

        private bool CombatActorRoleAvailable(ResidentActor actor, bool requireWeapon)
        {
            if (!actor || actor.Resident == null || !actor.Resident.IsAlive || actor.Resident.IsQuestProtected ||
                QuestOwned(actor.Resident) || actor.IsSleeping) return false;
            var role = worldStore.ReadNpcRole(actor.Resident.Id);
            if (role == null || role.CombatRole == NpcCombatRole.Unknown)
            {
                PeopleOfTheIliacBayMod.RefreshSavedNpcRole(worldStore, actor.Resident.Id);
                role = worldStore.ReadNpcRole(actor.Resident.Id);
            }
            if (!NpcRolePolicy.CanDirectCombatAssist(role)) return false;
            if (!requireWeapon) return true;
            DaggerfallWorkshop.Game.Items.DaggerfallUnityItem weapon;
            return NativeNpcCombatAssist.TryWeapon(actor, out weapon);
        }

        // A threat alarm must not steal one exact, already-admitted direct combat action.
        // The normal alarm resumes immediately if target, authorization, role or readiness changes.
        private bool CombatActionOwnsThreatResponse(string personId, string threatId)
        {
            ResidentActor actor;
            return !string.IsNullOrEmpty(personId) && actors.TryGetValue(personId, out actor) &&
                CombatActionOwnsThreatResponse(actor, threatId);
        }

        private bool CombatActionOwnsThreatResponse(ResidentActor actor, string threatId)
        {
            if (!actor || string.IsNullOrEmpty(threatId) || worldStore == null) return false;
            var active = worldStore.ActiveNpcAction(actor.Resident.Id);
            if (active == null || !IsCombatAction(active.Kind) || NpcActionExecutor.IsTerminal(active) ||
                active.Admission == null || active.Admission.Kind != NpcOperationAdmissionKind.Conversation ||
                active.Target == null || active.Target.Kind != NpcActionTargetKind.Person || active.Target.PersonId != threatId)
                return false;
            CombatActionMotion motion;
            bool retreating = combatActionMotions.TryGetValue(actor.Resident.Id, out motion) &&
                motion.JobId == active.Id && motion.Retreating;
            if (!CombatActorRoleAvailable(actor, !retreating)) return false;
            var target = NativeNpcCombatAssist.Find(threatId);
            return CombatTargetAvailable(actor, target, threatId, false);
        }

        private bool CombatTargetAvailable(ResidentActor actor, DaggerfallEntityBehaviour target, string id, bool starting)
        {
            return id != null && NativeNpcCombatAssist.Identity(target) == id &&
                NativeNpcCombatAssist.OwnershipClear(target) &&
                NativeNpcCombatAssist.Unprovoked(worldStore, id, population.SettlementId) &&
                NativeNpcCombatAssist.ThreatensPlayerOrActor(target, actor, starting) &&
                NativeNpcCombatAssist.SameVisibleScene(actor, target, 20);
        }

        private bool PrepareCombatAction(ResidentActor actor, NpcActionJob job, out string reason)
        {
            reason = "Only an equipped, willing fighter can assist against this exact observed native attacker.";
            if (job == null || !IsCombatAction(job.Kind) || job.Target == null ||
                job.Target.Kind != NpcActionTargetKind.Person || !CombatActorAvailable(actor, job.Target.PersonId)) return false;
            var target = NativeNpcCombatAssist.Find(job.Target.PersonId);
            if (!CombatTargetAvailable(actor, target, job.Target.PersonId, true)) return false;
            reason = null;
            return true;
        }

        private bool ApplyCombatAction(ResidentActor actor, NpcActionJob job, ActionMotion motion, double now)
        {
            CombatActionMotion combat;
            if (!combatActionMotions.TryGetValue(job.ResidentId, out combat) || combat.JobId != job.Id)
            {
                combat = new CombatActionMotion { JobId = job.Id, BeganAt = Time.unscaledTime,
                    Epoch = worldStore.CaptureNpcVitalsWriteEpoch(),
                    Target = NativeNpcCombatAssist.Find(job.Target == null ? null : job.Target.PersonId) };
                combatActionMotions[job.ResidentId] = combat;
            }
            var target = combat.Target;
            if (target && (job.Target == null || NativeNpcCombatAssist.Identity(target) != job.Target.PersonId)) target = null;
            bool deathObserved = combat.SawExactTarget && ObserveExactCombatDeath(actor, job);
            bool authorized = CombatActionAuthorized(actor, job, combat, !deathObserved && !combat.Retreating);
            if (target && CombatTargetAvailable(actor, target, job.Target.PersonId, false)) combat.SawExactTarget = true;
            var facts = new NpcCombatAssistFacts { ActorAlive = actor && actor.Resident.IsAlive,
                TargetPresent = target && target.Entity != null,
                TargetHealth = target && target.Entity != null ? target.Entity.CurrentHealth : -1,
                NativeDeathObserved = deathObserved,
                ConsentCurrent = authorized, OwnershipClear = authorized && (deathObserved || NativeNpcCombatAssist.OwnershipClear(target)),
                SameScene = deathObserved || NativeNpcCombatAssist.SameVisibleScene(actor, target, 20),
                TargetHostile = target && target.GetComponent<EnemyMotor>() && target.GetComponent<EnemyMotor>().IsHostile,
                TargetAttackingProtectedSubject = NativeNpcCombatAssist.ThreatensPlayerOrActor(target, actor, false),
                UnprovokedAttack = NativeNpcCombatAssist.Unprovoked(worldStore, job.Target == null ? null : job.Target.PersonId,
                    population.SettlementId), ElapsedSeconds = Time.unscaledTime - combat.BeganAt };
            var observed = NpcCombatAssistPolicy.Observe(facts);
            if (observed == NpcCombatAssistObservation.Interrupted || now >= job.DeadlineAt)
            {
                EndAction(job, now, "Combat assistance stopped: the exact attacker or authorization is no longer available.", true);
                combatActionMotions.Remove(job.ResidentId);
                return true;
            }
            if (observed == NpcCombatAssistObservation.TargetDefeated)
            {
                if (job.Phase != NpcActionPhase.Executing)
                {
                    worldStore.ObserveNpcAction(job.Id, job.Revision, now, NpcActionObservation.Arrival());
                    job = worldStore.ReadNpcAction(job.Id);
                }
                if (worldStore.ObserveNpcAction(job.Id, job.Revision, now, NpcActionObservation.Success(job.Id + ":target-defeated")))
                {
                    job = worldStore.ReadNpcAction(job.Id);
                    RememberActionOutcome(job, now);
                    NotifyActionOutcome(job, actor, null);
                    worldStore.SaveNpcActionPosition(job.Id, job.Revision,
                        ActionPosition(actor.transform.position - Vector3.up), true, now);
                }
                combatActionMotions.Remove(job.ResidentId);
                return true;
            }
            var role = worldStore.ReadNpcRole(job.ResidentId);
            var body = actor.GetComponent<DaggerfallEntityBehaviour>();
            var native = target.GetComponent<DaggerfallEnemy>();
            int roll;
            if (NativeNpcCombatRoleDecision.TryGetEncounterRoll(worldStore.WorldSeed, 0, job.ResidentId,
                    native ? native.LoadID : 0, job.Target.PersonId, out roll) &&
                NpcRolePolicy.ShouldRetreat(role, body.Entity.CurrentHealth, body.Entity.MaxHealth, roll))
                combat.Retreating = true;
            if (combat.Retreating) return RetreatCombatAction(actor, target, job, motion, now);

            Vector3 goal = target.transform.position - Vector3.up;
            float distance = Vector3.Distance(actor.transform.position, target.transform.position);
            if (distance <= NativeNpcCombatAssist.MeleeRange)
            {
                motion.Indoor = null; motion.Outdoor = null; motion.NativeOutdoor = false;
                actor.Place(actor.transform.position - Vector3.up, target.transform.position - actor.transform.position, true);
                if (job.Phase != NpcActionPhase.Executing)
                {
                    worldStore.ObserveNpcAction(job.Id, job.Revision, now, NpcActionObservation.Arrival());
                    job = worldStore.ReadNpcAction(job.Id);
                }
                if (Time.unscaledTime >= combat.NextSwingAt)
                {
                    // MobilePersonBillboard has no native enemy attack animation. Keep its
                    // existing counterattack cadence; all hit/damage/equipment rules are native.
                    combat.NextSwingAt = Time.unscaledTime + 1.5f;
                    var current = job;
                    int damage;
                    NativeNpcCombatAssist.TrySwing(actor,target,worldStore,now,
                        () => CombatActionAuthorized(actor, current, combat) &&
                            CombatTargetAvailable(actor, target, current.Target.PersonId, false), out damage);
                }
                motion.ProgressAt = Time.unscaledTime;
            }
            else
            {
                string reason;
                if (Time.unscaledTime >= motion.RepathAt &&
                    (motion.Indoor == null && motion.Outdoor == null || Vector3.Distance(motion.Goal, goal) > 0.5f ||
                        motion.Indoor != null && motion.Indoor.Arrived || motion.Outdoor != null && motion.Next >= motion.Outdoor.Count))
                {
                    if (!BuildActionRoute(actor, motion, goal, 0.4f, out reason))
                    { EndAction(job, now, reason, true); combatActionMotions.Remove(job.ResidentId); return true; }
                }
                if (job.Phase == NpcActionPhase.Accepted || job.Phase == NpcActionPhase.Executing)
                {
                    worldStore.ObserveNpcAction(job.Id, job.Revision, now, NpcActionObservation.Approach());
                    job = worldStore.ReadNpcAction(job.Id);
                }
                if (Vector3.Distance(actor.transform.position, motion.LastProgress) >= 0.15f)
                { motion.LastProgress = actor.transform.position; motion.ProgressAt = Time.unscaledTime; }
                if (Time.unscaledTime - motion.ProgressAt > 20)
                { EndAction(job, now, "The combat approach remained blocked.", true); combatActionMotions.Remove(job.ResidentId); return true; }
                AdvanceActionMotion(actor, motion);
            }
            if (Time.unscaledTime >= motion.SaveAt)
            {
                motion.SaveAt = Time.unscaledTime + 0.5f;
                worldStore.SaveNpcActionPosition(job.Id, job.Revision,
                    ActionPosition(actor.transform.position - Vector3.up), false, now);
            }
            return true;
        }

        private bool CombatActionAuthorized(ResidentActor actor, NpcActionJob job, CombatActionMotion combat, bool requireWeapon = true)
        {
            if (!worldStore.IsNpcVitalsWriteEpochCurrent(combat.Epoch) || !CombatActorAvailable(actor,
                job == null || job.Target == null ? null : job.Target.PersonId, requireWeapon) ||
                job == null || job.Admission == null || job.Admission.Kind != NpcOperationAdmissionKind.Conversation ||
                !ActionSpaceMatches(job.LastPosition)) return false;
            var active = worldStore.ActiveNpcAction(job.ResidentId);
            return active != null && active.Id == job.Id && !NpcActionExecutor.IsTerminal(active) &&
                active.Target != null && active.Target.PersonId == job.Target.PersonId;
        }

        private bool ObserveExactCombatDeath(ResidentActor actor, NpcActionJob job)
        {
            if (!actor || job.Target == null) return false;
            var record = worldStore.GetDefenseCombatant(job.Target.PersonId);
            var death = record == null ? null : record.Death;
            if (death == null || record.PlayerProvoked || record.SettlementId != population.SettlementId ||
                death.DiedAt < job.AcceptedAt || death.BuildingKey != (interior ? interior.EntryDoor.buildingKey : -1)) return false;
            var root = interior ? interior.transform : directory.transform;
            var point = root.TransformPoint(new Vector3((float)death.X, (float)death.Y, (float)death.Z)) + Vector3.up;
            return CivilianResponses.Sees(actor.transform.position, point, 20);
        }

        private bool RetreatCombatAction(ResidentActor actor, DaggerfallEntityBehaviour target,
            NpcActionJob job, ActionMotion motion, double now)
        {
            motion.Indoor = null; motion.Outdoor = null; motion.NativeOutdoor = false;
            var ground = actor.transform.position - Vector3.up;
            var away = ground - target.transform.position; away.y = 0;
            if (away.sqrMagnitude < 0.01f) away = -actor.transform.forward;
            Vector3 retreat = ground;
            if (!interior && geometry != null)
            {
                var steering = actor.GetComponent<CivilianFlightSteering>() ?? actor.gameObject.AddComponent<CivilianFlightSteering>();
                retreat = steering.Target(ground, ground + away.normalized * 4, target.transform.position, geometry, directory.transform);
            }
            else CivilianFlightSteering.TryDirectRetreat(ground, ground + away.normalized * 3, out retreat);
            actor.Place(Vector3.MoveTowards(ground, retreat, (float)ResidentMovement.FleeSpeed(actor.Resident.AppearanceSeed) * Time.deltaTime),
                retreat - ground, fleeing: true);
            job = worldStore.ReadNpcAction(job.Id);
            worldStore.SaveNpcActionPosition(job.Id, job.Revision, ActionPosition(actor.transform.position - Vector3.up), false, now);
            if (Vector3.Distance(actor.transform.position, target.transform.position) >= 8)
            {
                job = worldStore.ReadNpcAction(job.Id);
                EndAction(job, now, "The seriously wounded fighter withdrew from combat.", true);
                combatActionMotions.Remove(job.ResidentId);
            }
            return true;
        }
    }
}
