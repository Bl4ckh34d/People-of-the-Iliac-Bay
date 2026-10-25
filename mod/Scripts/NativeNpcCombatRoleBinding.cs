using System;
using System.Globalization;
using PeopleOfTheIliacBay.Core;

#if !NATIVE_NPC_COMBAT_ROLE_TEST
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Utility;
using UnityEngine;
#endif

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>Pure, stable decision seams shared by the native binding and its regression tests.</summary>
    internal static class NativeNpcCombatRoleDecision
    {
        internal const string PlayerTargetIdentity = "player";
        private const int LastStandardClassCareerIndex = 17;

        internal static bool TryMapCareer(int careerIndex, out NpcCombatRole role)
        {
            role = NpcCombatRole.Unknown;
            if (careerIndex < 0 || careerIndex > LastStandardClassCareerIndex) return false;

            switch (careerIndex)
            {
                case 8: role = NpcCombatRole.Rogue; return true;
                case 11: role = NpcCombatRole.Assassin; return true;
                case 16: role = NpcCombatRole.Warrior; return true;
                default:
                    // All other standard ClassCareers are the explicitly approved generic
                    // combatant bucket. Non-class/custom IDs never reach this mapping.
                    role = NpcCombatRole.Combatant;
                    return true;
            }
        }

        internal static bool TryGetEncounterRoll(uint worldSeed, ulong actorLoadId, string stableActorId,
            ulong targetLoadId, string stableTargetId, out int roll)
        {
            roll = 0;
            string actorIdentity = actorLoadId != 0 ? "load:" + actorLoadId.ToString(CultureInfo.InvariantCulture) :
                StableIdentity(stableActorId);
            string targetIdentity = targetLoadId != 0 ? "load:" + targetLoadId.ToString(CultureInfo.InvariantCulture) :
                StableIdentity(stableTargetId);
            if (actorIdentity == null || targetIdentity == null) return false;

            ulong hash = 14695981039346656037UL;
            hash = AddUInt32(hash, worldSeed);
            hash = AddText(hash, actorIdentity);
            hash = AddText(hash, targetIdentity);
            hash += 0x9E3779B97F4A7C15UL;
            hash = (hash ^ (hash >> 30)) * 0xBF58476D1CE4E5B9UL;
            hash = (hash ^ (hash >> 27)) * 0x94D049BB133111EBUL;
            hash ^= hash >> 31;
            roll = (int)(hash % 100UL);
            return true;
        }

        internal static bool ShouldApplyRetreat(NpcRoleState role, NpcCombatRole nativeCareerRole,
            bool actorQuestOwned, bool targetProtected, int health, int maximumHealth, int encounterRoll)
        {
            if (role == null || actorQuestOwned || targetProtected || role.CombatRole != nativeCareerRole)
                return false;

            try { NpcRoleState.CopyValidated(role); }
            catch (ArgumentException) { return false; }

            return NpcRolePolicy.ShouldRetreat(role, health, maximumHealth, encounterRoll);
        }

        internal static bool IsCurrentActorRoleEligible(NpcCombatRole savedRole,
            NpcCombatRole currentRole, bool isPlayerAlly)
        {
            return !isPlayerAlly && savedRole != NpcCombatRole.Unknown && savedRole == currentRole;
        }

        internal static bool IsAdmittedCombatTarget(bool playerTarget, bool hostileToPlayer,
            bool detectedTarget, int giveUpTimer)
        {
            return playerTarget ? hostileToPlayer : detectedTarget || giveUpTimer > 0;
        }

        // EnemySenses and EnemyMotor read a target CharacterController. Managed
        // residents have a CapsuleCollider instead, from which an equivalent,
        // disabled target controller can be prepared for native guard combat.
        internal static bool CanSupplyNativeTargetController(bool hasController, bool hasCapsule)
        {
            return hasController || hasCapsule;
        }

        internal static float GroundPlaneYFromControllerBounds(float boundsMinY)
        {
            // CharacterController.bounds already describes the live world-space collider.
            // Keep the ground sample on its lower face; CivilianFlightSteering validates
            // support at this height and must retain its slope/cliff checks.
            return boundsMinY;
        }

        private static string StableIdentity(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > NpcRoleState.MaximumSourceIdLength)
                return null;
            for (int i = 0; i < value.Length; i++)
                if (char.IsControl(value[i])) return null;
            return string.Equals(value, PlayerTargetIdentity, StringComparison.Ordinal) ?
                "target:player" : "stable:" + value;
        }

        private static ulong AddUInt32(ulong hash, uint value)
        {
            for (int i = 0; i < 4; i++)
            {
                hash ^= (byte)(value & 0xff);
                hash *= 1099511628211UL;
                value >>= 8;
            }
            return hash;
        }

        private static ulong AddText(ulong hash, string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                ushort character = value[i];
                hash ^= (byte)(character & 0xff);
                hash *= 1099511628211UL;
                hash ^= (byte)(character >> 8);
                hash *= 1099511628211UL;
            }
            hash ^= 0xff;
            return hash * 1099511628211UL;
        }
    }

#if !NATIVE_NPC_COMBAT_ROLE_TEST
    /// <summary>
    /// Adds only a bounded escape step to the native enemy action callback when saved role,
    /// current native class, service, health, target and encounter pair all authorize it.
    /// </summary>
    internal sealed class NativeNpcCombatRoleBinding : MonoBehaviour
    {
        private const float RetreatSpeed = 1.15f;
        private const float MaximumStepDistance = 0.08f;
        private const float DirectCorridorLength = 1.25f;

        private DaggerfallEntityBehaviour actor;
        private EnemyMotor motor;
        private DaggerfallEnemy nativeEnemy;
        private int boundNativeMobileId;
        private NpcRoleState role;
        private NpcCombatRole liveCareerRole;
        private uint worldSeed;
        private string stableActorId;
        private Func<DaggerfallEntityBehaviour, string> stableTargetIdResolver;
        private Func<DaggerfallEntityBehaviour, Vector3?> cityWallSafeWaypointResolver;
        private EnemyMotor.TakeActionCallback originalTakeAction;
        private EnemyMotor.TakeActionCallback installedTakeAction;
        private bool configured;
        private bool ownershipLost;

        internal static NativeNpcCombatRoleBinding Bind(DaggerfallEntityBehaviour actor, NpcRoleState role,
            uint worldSeed, string stableActorId = null,
            Func<DaggerfallEntityBehaviour, string> stableTargetIdResolver = null,
            Func<DaggerfallEntityBehaviour, Vector3?> cityWallSafeWaypointResolver = null)
        {
            if (!actor || role == null) return null;
            NativeNpcCombatRoleBinding binding = actor.GetComponent<NativeNpcCombatRoleBinding>();
            bool created = binding == null;
            if (created) binding = actor.gameObject.AddComponent<NativeNpcCombatRoleBinding>();
            if (binding.Configure(actor, role, worldSeed, stableActorId, stableTargetIdResolver,
                cityWallSafeWaypointResolver)) return binding;
            if (created && binding) UnityEngine.Object.Destroy(binding);
            return null;
        }

        internal bool Configure(DaggerfallEntityBehaviour boundActor, NpcRoleState savedRole,
            uint seed, string actorStableId = null,
            Func<DaggerfallEntityBehaviour, string> targetStableIdResolver = null,
            Func<DaggerfallEntityBehaviour, Vector3?> cityWallWaypointResolver = null)
        {
            if (ownershipLost) return false;
            if (configured)
            {
                if (motor && installedTakeAction != null && motor.TakeActionHandler != installedTakeAction)
                {
                    ownershipLost = true;
                    ForgetHandler();
                    return false;
                }
                RestoreOwnedHandler();
            }

            if (!boundActor || boundActor.EntityType != EntityTypes.EnemyClass || boundActor.Entity == null ||
                savedRole == null) return false;
            NpcRoleState validated;
            try { validated = NpcRoleState.CopyValidated(savedRole); }
            catch (ArgumentException) { return false; }

            EnemyEntity entity = boundActor.Entity as EnemyEntity;
            if (entity == null) return false;
            NpcCombatRole effectiveRole;
            if (!TryResolveEffectiveRole(boundActor, entity, validated, out effectiveRole)) return false;

            if (!string.IsNullOrWhiteSpace(actorStableId) &&
                !string.Equals(validated.PersonId, actorStableId, StringComparison.Ordinal)) return false;

            actor = boundActor;
            nativeEnemy = boundActor.GetComponent<DaggerfallEnemy>();
            if (!nativeEnemy) return false;
            boundNativeMobileId = entity.MobileEnemy.ID;
            motor = boundActor.GetComponent<EnemyMotor>();
            if (!motor) return false;
            role = validated;
            liveCareerRole = effectiveRole;
            worldSeed = seed;
            stableActorId = actorStableId;
            stableTargetIdResolver = targetStableIdResolver;
            cityWallSafeWaypointResolver = cityWallWaypointResolver;
            configured = true;
            enabled = true;
            TryInstallHandler();
            return true;
        }

        private void LateUpdate()
        {
            if (!configured || ownershipLost) return;
            if (installedTakeAction != null)
            {
                if (motor && motor.TakeActionHandler != installedTakeAction)
                {
                    ownershipLost = true;
                    ForgetHandler();
                }
                return;
            }
            TryInstallHandler();
        }

        private void OnEnable()
        {
            if (configured && !ownershipLost) TryInstallHandler();
        }

        private void OnDisable()
        {
            if (motor && installedTakeAction != null && motor.TakeActionHandler != installedTakeAction)
            {
                ownershipLost = true;
                ForgetHandler();
                return;
            }
            RestoreOwnedHandler();
        }

        private void OnDestroy()
        {
            if (motor && installedTakeAction != null && motor.TakeActionHandler == installedTakeAction)
                motor.TakeActionHandler = originalTakeAction;
            ForgetHandler();
        }

        private void TryInstallHandler()
        {
            if (!configured || ownershipLost || !motor || installedTakeAction != null) return;
            EnemyMotor.TakeActionCallback current = motor.TakeActionHandler;
            if (current == null || !ReferenceEquals(current.Target, motor) ||
                !string.Equals(current.Method.Name, "TakeAction", StringComparison.Ordinal)) return;
            originalTakeAction = current;
            installedTakeAction = TakeAction;
            motor.TakeActionHandler = installedTakeAction;
        }

        private void TakeAction()
        {
            EnemyMotor.TakeActionCallback original = originalTakeAction;
            if (original == null) return;
            if (TryApplyRetreatStep()) return;
            NativeDelegatedActionCount++;
            original();
        }

        private bool TryApplyRetreatStep()
        {
            if (!configured || !actor || !motor || !nativeEnemy || !actor.gameObject.activeInHierarchy ||
                !motor.enabled) return false;
            GuardActor managedGuard = actor.GetComponent<GuardActor>();
            if (managedGuard && managedGuard.HasActiveCombatRefuge(actor)) return true;
            if (GameManager.IsGamePaused || actor.Entity == null || actor.Entity.CurrentHealth <= 0 ||
                actor.EntityType != EntityTypes.EnemyClass || nativeEnemy.QuestSpawn ||
                actor.GetComponent<QuestResourceBehaviour>() != null) return false;
            NpcCombatRole currentRole;
            EnemyEntity currentNativeEntity = actor.Entity as EnemyEntity;
            bool playerAlly = actor.Entity.Team == MobileTeams.PlayerAlly;
            if (playerAlly || currentNativeEntity == null ||
                currentNativeEntity.MobileEnemy.ID != boundNativeMobileId ||
                !TryResolveEffectiveRole(actor, currentNativeEntity, role, out currentRole) ||
                !NativeNpcCombatRoleDecision.IsCurrentActorRoleEligible(role.CombatRole, currentRole, playerAlly))
                return false;

            EnemySenses senses = actor.GetComponent<EnemySenses>();
            CharacterController controller = actor.GetComponent<CharacterController>();
            DaggerfallEntityBehaviour target = senses ? senses.Target : null;
            if (!senses || !target || target == actor || !controller || !controller.enabled ||
                target.Entity == null || target.Entity.CurrentHealth <= 0) return false;
            bool playerTarget = target.EntityType == EntityTypes.Player;
            if (!NativeNpcCombatRoleDecision.IsAdmittedCombatTarget(playerTarget, motor.IsHostile,
                senses.DetectedTarget, motor.GiveUpTimer)) return false;

            QuestResourceBehaviour protectedQuestTarget = target.GetComponent<QuestResourceBehaviour>();
            if (protectedQuestTarget && !protectedQuestTarget.IsAttackableByAI) return false;

            DaggerfallEnemy targetEnemy = target.GetComponent<DaggerfallEnemy>();
            ulong targetLoadId = targetEnemy ? targetEnemy.LoadID : 0;
            string targetStableId = target.EntityType == EntityTypes.Player ?
                NativeNpcCombatRoleDecision.PlayerTargetIdentity :
                (stableTargetIdResolver == null ? null : stableTargetIdResolver(target));
            int roll;
            if (!NativeNpcCombatRoleDecision.TryGetEncounterRoll(worldSeed, nativeEnemy.LoadID,
                stableActorId, targetLoadId, targetStableId, out roll)) return false;

            bool isQuestOwned = nativeEnemy.QuestSpawn || actor.GetComponent<QuestResourceBehaviour>() != null;
            bool isTargetProtected = protectedQuestTarget && !protectedQuestTarget.IsAttackableByAI;
            if (!NativeNpcCombatRoleDecision.ShouldApplyRetreat(role, liveCareerRole, isQuestOwned,
                isTargetProtected, actor.Entity.CurrentHealth, actor.Entity.MaxHealth, roll)) return false;

            Bounds controllerBounds = controller.bounds;
            Vector3 origin = new Vector3(controllerBounds.center.x,
                NativeNpcCombatRoleDecision.GroundPlaneYFromControllerBounds(controllerBounds.min.y),
                controllerBounds.center.z);
            bool cityWatch = currentNativeEntity.MobileEnemy.ID == (int)MobileTypes.Knight_CityWatch;
            if (role.CombatRole == NpcCombatRole.Guard || cityWatch)
            {
                // GuardPopulation owns the surveyed wall/gate route and persists refuge.
                // A non-null waypoint means that route was accepted; do not also move the
                // controller here. Missing route ownership leaves native combat untouched.
                if (!cityWatch || role.CombatRole != NpcCombatRole.Guard) return false;
                if (cityWallSafeWaypointResolver == null) return false;
                Vector3? waypoint = cityWallSafeWaypointResolver(actor);
                if (!waypoint.HasValue) return false;
                return true;
            }

            Vector3 away = origin - target.transform.position;
            away.y = 0;
            if (away.sqrMagnitude < 0.0001f) return false;
            Vector3 preferred = origin + away.normalized * DirectCorridorLength;
            Vector3 safeEnd;
            if (!CivilianFlightSteering.TryDirectRetreat(origin, preferred, out safeEnd)) return false;
            Vector3 route = safeEnd - origin;
            route.y = 0;
            float available = route.magnitude;
            float step = Mathf.Min(RetreatSpeed * Time.fixedDeltaTime, MaximumStepDistance);
            if (available < 0.01f || step <= 0.001f) return false;

            Vector3 before = controller.transform.position;
            controller.Move(route.normalized * Mathf.Min(step, available));
            Vector3 moved = controller.transform.position - before;
            moved.y = 0;
            return moved.sqrMagnitude > 0.000025f;
        }

        private void RestoreOwnedHandler()
        {
            if (motor && installedTakeAction != null && motor.TakeActionHandler == installedTakeAction)
                motor.TakeActionHandler = originalTakeAction;
            ForgetHandler();
        }

        private void ForgetHandler()
        {
            originalTakeAction = null;
            installedTakeAction = null;
        }

        internal int NativeDelegatedActionCount { get; private set; }

        internal bool OwnsTakeActionHandler
        {
            get { return motor && installedTakeAction != null && motor.TakeActionHandler == installedTakeAction; }
        }

        private static bool TryResolveEffectiveRole(DaggerfallEntityBehaviour candidate,
            EnemyEntity entity, NpcRoleState savedRole, out NpcCombatRole effectiveRole)
        {
            effectiveRole = NpcCombatRole.Unknown;
            if (!candidate || entity == null || savedRole == null) return false;

            bool cityWatchNative = entity.MobileEnemy.ID == (int)MobileTypes.Knight_CityWatch;
            bool explicitNativeDuty = savedRole.CombatSourceId != null &&
                savedRole.CombatSourceId.StartsWith("native:", StringComparison.Ordinal);
            if (cityWatchNative)
            {
                if (!explicitNativeDuty) return false;
                if (savedRole.CombatRole == NpcCombatRole.Guard)
                    effectiveRole = NpcCombatRole.Guard;
                else if (savedRole.CombatRole == NpcCombatRole.PalaceGuard &&
                    savedRole.ServiceBinding == NpcServiceBinding.NobleKingdom)
                    effectiveRole = NpcCombatRole.PalaceGuard;
                else
                    return false;
                return true;
            }

            NpcCombatRole mappedRole;
            if (!NativeNpcCombatRoleDecision.TryMapCareer(entity.MobileEnemy.ID - 128, out mappedRole))
                return false;
            if (savedRole.CombatRole == mappedRole)
            {
                effectiveRole = mappedRole;
                return true;
            }

            bool guardActor = candidate.GetComponent<GuardActor>() != null;
            if (savedRole.CombatRole == NpcCombatRole.Guard && guardActor && explicitNativeDuty)
                effectiveRole = NpcCombatRole.Guard;
            else if (savedRole.CombatRole == NpcCombatRole.PalaceGuard && guardActor &&
                savedRole.ServiceBinding == NpcServiceBinding.NobleKingdom && explicitNativeDuty)
                effectiveRole = NpcCombatRole.PalaceGuard;
            else if (savedRole.CombatRole == NpcCombatRole.Soldier && explicitNativeDuty)
                effectiveRole = NpcCombatRole.Soldier;
            else
                return false;
            return true;
        }
    }
#endif
}
