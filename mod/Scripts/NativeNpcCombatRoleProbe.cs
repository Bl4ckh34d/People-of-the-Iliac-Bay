using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>
    /// Controlled native EnemyMotor acceptance probe. All actors and geometry are disposable,
    /// isolated above the current exterior and destroyed before the probe returns.
    /// </summary>
    internal static class NativeNpcCombatRoleProbe
    {
        private const string ProbeIdentityPrefix = "native-combat-role-probe:";
        private const float SevereHealthRatio = 0.25f;
        private const float ObservationSeconds = 0.75f;
        private const float MinimumObservedRetreat = 0.04f;

        private sealed class NativeFixture
        {
            internal GameObject Root;
            internal DaggerfallEntityBehaviour Behaviour;
            internal DaggerfallEnemy Native;
            internal EnemyMotor Motor;
            internal EnemySenses Senses;
            internal EnemyAttack Attack;
            internal CharacterController Controller;
            internal NpcRoleState Role;
            internal string ActorId;
            internal string InitialHandlerDescription;
            internal int InitialRootInstanceId;
            internal bool InitialBindingEnabled;
            internal bool InitialBindingOwnsHandler;
            internal bool InitialMotorEnabled;
            internal int InitialMobileId;
            internal ulong InitialLoadId;
            internal int InitialHealth;
            internal int InitialMaxHealth;
            internal Vector3 StartPosition;
            internal float StartThreatGap;
            internal NativeNpcCombatRoleBinding Binding;

            internal int NativeActionCalls
            {
                get { return Binding == null ? 0 : Binding.NativeDelegatedActionCount; }
            }
        }

        internal static IEnumerator Run(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            uint worldSeed)
        {
            Require(host && streets && host.GetComponent<StreetPopulation>() == streets,
                "Native combat-role probe requires its loaded host and owned StreetPopulation.");
            GameManager game = GameManager.Instance;
            Require(game != null && !game.PlayerEnterExit.IsPlayerInside && !GameManager.IsGamePaused,
                "Native combat-role probe requires an unpaused exterior scene.");

            var owned = new List<GameObject>();
            GameObject floor = null;
            PlayerEntity.Crimes backupCrime = game.PlayerEntity.CrimeCommitted;
            bool restoreCrime = false;
            try
            {
                Vector3 platform = game.PlayerMotor.transform.position + Vector3.up * 120f;
                floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.name = "Disposable native combat-role test platform";
                floor.hideFlags = HideFlags.HideAndDontSave;
                floor.transform.position = platform - Vector3.up * 0.1f;
                floor.transform.localScale = new Vector3(24f, 0.2f, 18f);
                Physics.SyncTransforms();

                GameObject targetObject = SpawnEnemy(MobileTypes.Thief, platform + Vector3.right * 3f,
                    "Disposable inert native combat-role target");
                owned.Add(targetObject);
                yield return null;
                DaggerfallEntityBehaviour target = PrepareInertTarget(targetObject,
                    platform + Vector3.right * 3f, platform.y);

                // Independent severe-injury Rogue: choose a stable disposable fixture identity
                // whose deterministic encounter pair falls inside the authorized 35% window.
                string rogueId = FindEligibleActorId(worldSeed, "rogue-independent", target, "probe-target", 35);
                GameObject rogueRoot = SpawnEnemy(MobileTypeForCareer(8), platform,
                    "Disposable independent Rogue combat-role actor");
                owned.Add(rogueRoot);
                yield return null;
                NativeFixture rogue = CreateFixture(rogueRoot, target,
                    Role(rogueId, NpcCombatRole.Rogue, NpcServiceBinding.Independent), rogueId,
                    severe: true);
                PrepareActor(rogue, platform, target, worldSeed, "probe-target", false, null);
                float rogueGapBefore = Vector3.Distance(rogue.Root.transform.position, target.transform.position);
                Vector3 away = rogue.Root.transform.position - target.transform.position;
                away.y = 0;
                Require(away.sqrMagnitude > 0.001f, "Rogue fixture target cannot define an away direction.");
                away.Normalize();
                rogue.StartPosition = rogue.Root.transform.position;
                rogue.StartThreatGap = rogueGapBefore;
                float deadline = Time.realtimeSinceStartup + ObservationSeconds;
                while (Time.realtimeSinceStartup < deadline) yield return null;
                Vector3 rogueMotion = rogue.Root.transform.position - rogue.StartPosition;
                rogueMotion.y = 0;
                float rogueGapAfter = Vector3.Distance(rogue.Root.transform.position, target.transform.position);
                CharacterController rogueController = rogue.Root.GetComponent<CharacterController>();
                Bounds rogueBounds = rogueController ? rogueController.bounds : new Bounds();
                Require(rogue.NativeActionCalls == 0 && Vector3.Dot(rogueMotion, away) >= MinimumObservedRetreat &&
                    rogueGapAfter >= rogue.StartThreatGap + MinimumObservedRetreat,
                    "Eligible independent Rogue did not take a bounded native retreat step; nativeCalls=" +
                    rogue.NativeActionCalls + "; motion=" + rogueMotion + "; gapBefore=" + rogue.StartThreatGap +
                    "; gapAfter=" + rogueGapAfter + "; rootY=" + rogue.Root.transform.position.y +
                    "; controllerBoundsMinY=" + rogueBounds.min.y + "; controllerSkinWidth=" +
                    (rogueController ? rogueController.skinWidth : -1f) + "; policyRoll=" +
                    EncounterRoll(worldSeed, rogue, target, "probe-target") + ".");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_COMBAT_ROLE_CASE_PASS: case=independent-rogue-severe; " +
                    "roll=" + EncounterRoll(worldSeed, rogue, target, "probe-target") + "; moved-away=" +
                    Vector3.Dot(rogueMotion, away).ToString("F3", System.Globalization.CultureInfo.InvariantCulture) +
                    "; native-action-calls=" + rogue.NativeActionCalls + "; exact-native-target=true.");
                DestroyOwned(rogue.Root, owned);

                // Healthy independent combatant still delegates to the native action callback.
                string healthyId = FindEligibleActorId(worldSeed, "rogue-healthy", target, "probe-target", 35);
                GameObject healthyRoot = SpawnEnemy(MobileTypeForCareer(8), platform,
                    "Disposable healthy Rogue combat-role actor");
                owned.Add(healthyRoot);
                yield return null;
                NativeFixture healthy = CreateFixture(healthyRoot, target,
                    Role(healthyId, NpcCombatRole.Rogue, NpcServiceBinding.Independent), healthyId,
                    severe: false);
                PrepareActor(healthy, platform, target, worldSeed, "probe-target", false, null);
                yield return ObserveNativeAction(healthy, "healthy-independent-rogue");
                DestroyOwned(healthy.Root, owned);

                // An unclassified service fact never becomes an inferred independent escape.
                string unknownServiceId = FindEligibleActorId(worldSeed, "rogue-unknown-service", target,
                    "probe-target", 35);
                GameObject unknownServiceRoot = SpawnEnemy(MobileTypeForCareer(8), platform,
                    "Disposable unknown-service Rogue combat-role actor");
                owned.Add(unknownServiceRoot);
                yield return null;
                NativeFixture unknownService = CreateFixture(unknownServiceRoot, target,
                    Role(unknownServiceId, NpcCombatRole.Rogue, NpcServiceBinding.Unknown), unknownServiceId,
                    severe: true);
                PrepareActor(unknownService, platform, target, worldSeed, "probe-target", false, null);
                yield return ObserveNativeAction(unknownService, "independent-service-unknown");
                DestroyOwned(unknownService.Root, owned);

                // Explicit noble service always keeps the native action for a Rogue.
                string nobleId = FindEligibleActorId(worldSeed, "rogue-noble-service", target, "probe-target", 35);
                GameObject nobleRoot = SpawnEnemy(MobileTypeForCareer(8), platform,
                    "Disposable noble-service Rogue combat-role actor");
                owned.Add(nobleRoot);
                yield return null;
                NativeFixture nobleService = CreateFixture(nobleRoot, target,
                    Role(nobleId, NpcCombatRole.Rogue, NpcServiceBinding.NobleKingdom), nobleId,
                    severe: true);
                PrepareActor(nobleService, platform, target, worldSeed, "probe-target", false, null);
                yield return ObserveNativeAction(nobleService, "noble-service-rogue");
                DestroyOwned(nobleService.Root, owned);

                // A passive native actor can have the player as its default sense target.
                // That alone is not an encounter and must not authorize role-based retreat.
                DaggerfallEntityBehaviour playerTarget = game.PlayerEntityBehaviour;
                string passivePlayerId = FindEligibleActorId(worldSeed, "passive-player-target",
                    playerTarget, "player", 35);
                GameObject passivePlayerRoot = SpawnEnemy(MobileTypeForCareer(8), platform,
                    "Disposable passive Rogue with default player target");
                owned.Add(passivePlayerRoot);
                yield return null;
                NativeFixture passivePlayer = CreateFixture(passivePlayerRoot, playerTarget,
                    Role(passivePlayerId, NpcCombatRole.Rogue, NpcServiceBinding.Independent), passivePlayerId,
                    severe: true);
                PrepareActor(passivePlayer, platform + Vector3.left * 3f, playerTarget, worldSeed,
                    "player", false, null, relocateTarget: false);
                // DFU can suppress the normal callback entirely for a passive player target.
                // This one case directly exercises the owned wrapper when CanAct rejects it.
                yield return ObserveNativeAction(passivePlayer, "nonhostile-default-player-target",
                    allowExplicitCallback: true);
                DestroyOwned(passivePlayer.Root, owned);

                // DFU despawns native Knight_CityWatch entities when crime is None. Hold a
                // temporary Assault state only across these two disposable CityWatch fixtures.
                restoreCrime = true;
                game.PlayerEntity.CrimeCommitted = PlayerEntity.Crimes.Assault;
                Require(game.PlayerEffectManager.IsTransformedLycanthrope() ||
                    game.PlayerEntity.CrimeCommitted == PlayerEntity.Crimes.Assault,
                    "Could not hold the temporary CityWatch fixture crime state.");

                // Every PalaceGuard roll remains non-retreating. The native scheduler must
                // invoke the owned callback naturally with this one-HP CityWatch fixture.
                var palaceRole = Role(ProbeIdentityPrefix + "palace-guard", NpcCombatRole.PalaceGuard,
                    NpcServiceBinding.NobleKingdom);
                for (int roll = 0; roll <= NpcRolePolicy.MaximumEncounterRoll; roll++)
                    Require(!NativeNpcCombatRoleDecision.ShouldApplyRetreat(palaceRole,
                        NpcCombatRole.PalaceGuard, false, false, 1, 100, roll),
                        "PalaceGuard unexpectedly retreated at roll=" + roll + ".");
                string palaceId = FindActorIdForRoll(worldSeed, "palace-guard-native", target,
                    "probe-target", 0);
                palaceRole.PersonId = palaceId;
                GameObject palaceRoot = SpawnEnemy(MobileTypes.Knight_CityWatch, platform,
                    "Disposable PalaceGuard combat-role actor");
                owned.Add(palaceRoot);
                yield return null;
                NativeFixture palaceGuard = CreateFixture(palaceRoot, target, palaceRole, palaceId,
                    severe: true, oneHp: true);
                PrepareActor(palaceGuard, platform, target, worldSeed, "probe-target", false, null);
                yield return ObserveNativeAction(palaceGuard, "palace-guard-one-hp-all-rolls-negative");
                DestroyOwned(palaceGuard.Root, owned);

                // City Watch has two eligible rolls at severe injury, but without an owned,
                // surveyed wall/gate waypoint it must continue native combat.
                for (int roll = 0; roll < NpcRolePolicy.CityGuardRetreatChancePercent; roll++)
                    Require(NativeNpcCombatRoleDecision.ShouldApplyRetreat(
                        Role("probe-city-watch-policy", NpcCombatRole.Guard, NpcServiceBinding.Unknown),
                        NpcCombatRole.Guard, false, false, 1, 100, roll),
                        "City Watch policy did not admit eligible 2% roll=" + roll + ".");
                string watchId = FindActorIdForRoll(worldSeed, "city-watch-no-wall-route", target,
                    "probe-target", 0);
                GameObject cityWatchRoot = SpawnEnemy(MobileTypes.Knight_CityWatch, platform,
                    "Disposable City Watch combat-role actor");
                owned.Add(cityWatchRoot);
                yield return null;
                NativeFixture cityWatch = CreateFixture(cityWatchRoot, target,
                    Role(watchId, NpcCombatRole.Guard, NpcServiceBinding.Unknown), watchId,
                    severe: true);
                PrepareActor(cityWatch, platform, target, worldSeed, "probe-target", false, null);
                yield return ObserveNativeAction(cityWatch, "city-watch-eligible-roll-missing-wall-route");
                DestroyOwned(cityWatch.Root, owned);

                game.PlayerEntity.CrimeCommitted = backupCrime;
                restoreCrime = false;

                // Quest-owned actors and protected quest targets both retain native callback.
                string questActorId = FindEligibleActorId(worldSeed, "quest-owned-rogue", target, "probe-target", 35);
                GameObject questActorRoot = SpawnEnemy(MobileTypeForCareer(8), platform,
                    "Disposable quest-owned Rogue combat-role actor");
                owned.Add(questActorRoot);
                yield return null;
                NativeFixture questActor = CreateFixture(questActorRoot, target,
                    Role(questActorId, NpcCombatRole.Rogue, NpcServiceBinding.Independent), questActorId,
                    severe: true);
                PrepareActor(questActor, platform, target, worldSeed, "probe-target", true, null);
                yield return ObserveNativeAction(questActor, "quest-owned-actor");
                DestroyOwned(questActor.Root, owned);

                QuestResourceBehaviour targetQuest = targetObject.GetComponent<QuestResourceBehaviour>();
                bool hadTargetQuest = targetQuest != null;
                if (!targetQuest) targetQuest = targetObject.AddComponent<QuestResourceBehaviour>();
                bool originalTargetAttackable = targetQuest.IsAttackableByAI;
                try
                {
                    targetQuest.IsAttackableByAI = false;
                    string protectedId = FindEligibleActorId(worldSeed, "protected-quest-target", target,
                        "probe-target", 35);
                    GameObject protectedRoot = SpawnEnemy(MobileTypeForCareer(8), platform,
                        "Disposable protected-target Rogue combat-role actor");
                    owned.Add(protectedRoot);
                    yield return null;
                    NativeFixture protectedTarget = CreateFixture(protectedRoot, target,
                        Role(protectedId, NpcCombatRole.Rogue, NpcServiceBinding.Independent), protectedId,
                        severe: true);
                    PrepareActor(protectedTarget, platform, target, worldSeed, "probe-target", false, null);
                    yield return ObserveNativeAction(protectedTarget, "protected-quest-target");
                    DestroyOwned(protectedTarget.Root, owned);
                }
                finally
                {
                    if (targetQuest)
                    {
                        if (hadTargetQuest) targetQuest.IsAttackableByAI = originalTargetAttackable;
                        else UnityEngine.Object.DestroyImmediate(targetQuest);
                    }
                }

                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_COMBAT_ROLE_PASS: independent-rogue-severe=physical-retreat; " +
                    "healthy-unknown-service-noble-service-palace-guard-city-watch-without-route-quest-owned-actor-" +
                    "protected-target=native-action; city-watch-policy=2-percent; palace-guard-rolls=0..99-never; " +
                    "palace-health=1; citywatch-crime-restored=true; wall-route-callback=not-provided-in-this-probe; " +
                    "player-position=unchanged; live-save-writes=none.");
            }
            finally
            {
                for (int i = owned.Count - 1; i >= 0; i--)
                    if (owned[i]) UnityEngine.Object.DestroyImmediate(owned[i]);
                if (restoreCrime && game != null && game.PlayerEntity != null)
                    game.PlayerEntity.CrimeCommitted = backupCrime;
                if (floor) UnityEngine.Object.DestroyImmediate(floor);
                Physics.SyncTransforms();
            }
        }

        private static IEnumerator ObserveNativeAction(NativeFixture fixture, string caseName,
            bool allowExplicitCallback = false)
        {
            int callsBefore = fixture.NativeActionCalls;
            float deadline = Time.realtimeSinceStartup + ObservationSeconds;
            while (Time.realtimeSinceStartup < deadline) yield return null;
            int scheduledCalls = fixture.NativeActionCalls - callsBefore;
            bool canActAtObservationEnd = fixture.Motor && fixture.Motor.CanAct;
            int giveUpTimerAtObservationEnd = fixture.Motor ? fixture.Motor.GiveUpTimer : -1;
            bool explicitCallback = false;
            if (scheduledCalls == 0)
            {
                if (!allowExplicitCallback)
                    throw new System.InvalidOperationException("No native-scheduled action for " + caseName +
                        ". " + DescribeCallbackState(fixture));
                if (fixture.Binding == null || !fixture.Binding.OwnsTakeActionHandler ||
                    !fixture.Motor || fixture.Motor.TakeActionHandler == null)
                    throw new System.InvalidOperationException("No native-scheduled action for " + caseName +
                        ", and the owned callback is unavailable. " + DescribeCallbackState(fixture));
                fixture.Motor.TakeActionHandler();
                explicitCallback = true;
            }
            Require(fixture.NativeActionCalls > callsBefore,
                "Native action callback was not delegated for " + caseName + "; calls=" + fixture.NativeActionCalls +
                "; scheduler-calls=" + scheduledCalls + "; CanAct=" + (fixture.Motor ? fixture.Motor.CanAct.ToString() : "n/a") +
                "; GiveUpTimer=" + (fixture.Motor ? fixture.Motor.GiveUpTimer.ToString() : "n/a") + "; target=" +
                (fixture.Senses.Target ? fixture.Senses.Target.name : "null") + ". " + DescribeCallbackState(fixture));
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NATIVE_COMBAT_ROLE_CASE_PASS: case=" + caseName +
                "; native-action-calls=" + fixture.NativeActionCalls + "; scheduler-calls=" + scheduledCalls +
                "; callback-source=" + (explicitCallback ? "explicit-owned-wrapper" : "native-scheduler") +
                "; CanAct-at-observation-end=" + canActAtObservationEnd +
                "; GiveUpTimer-at-observation-end=" + giveUpTimerAtObservationEnd +
                "; custom-retreat=false.");
        }

        private static NativeFixture CreateFixture(GameObject root, DaggerfallEntityBehaviour target,
            NpcRoleState role, string actorId, bool severe, bool oneHp = false)
        {
            var fixture = new NativeFixture
            {
                Root = root,
                Behaviour = root.GetComponent<DaggerfallEntityBehaviour>(),
                Native = root.GetComponent<DaggerfallEnemy>(),
                Motor = root.GetComponent<EnemyMotor>(),
                Senses = root.GetComponent<EnemySenses>(),
                Attack = root.GetComponent<EnemyAttack>(),
                Controller = root.GetComponent<CharacterController>(),
                Role = role,
                ActorId = actorId
            };
            Require(fixture.Behaviour && fixture.Native && fixture.Motor && fixture.Senses &&
                fixture.Attack && fixture.Controller, "Native enemy fixture missed a required DFU component.");
            Require(fixture.Behaviour.EntityType == EntityTypes.EnemyClass &&
                fixture.Behaviour.Entity is EnemyEntity,
                "Native role fixture did not create a real EnemyClass entity: " +
                (fixture.Behaviour.Entity as EnemyEntity).MobileEnemy.ID + ".");
            Require(fixture.Native.LoadID == 0,
                "Disposable native actor unexpectedly has a persistent LoadID; refusing unstable fixture identity.");
            Require(fixture.Motor.TakeActionHandler != null,
                "Native EnemyMotor has no original TakeActionHandler after its startup frame.");
            int maxHealth = fixture.Behaviour.Entity.MaxHealth;
            Require(maxHealth > 0, "Native combat fixture has no maximum health.");
            fixture.Behaviour.Entity.CurrentHealth = oneHp ? 1 :
                severe ? Math.Max(1, (int)Math.Floor(maxHealth * SevereHealthRatio)) : maxHealth;
            Require(!oneHp || fixture.Behaviour.Entity.CurrentHealth == 1,
                "PalaceGuard fixture did not retain the requested exact one-HP state.");
            fixture.Senses.Target = target;
            fixture.Senses.enabled = false;
            fixture.Attack.enabled = false;
            fixture.Motor.enabled = false;
            fixture.Controller.enabled = true;
            return fixture;
        }

        private static void PrepareActor(NativeFixture fixture, Vector3 platform,
            DaggerfallEntityBehaviour target, uint worldSeed, string targetId,
            bool actorQuestOwned, Func<DaggerfallEntityBehaviour, Vector3?> wallWaypointResolver,
            bool relocateTarget = true)
        {
            Require(fixture != null && fixture.Behaviour && fixture.Native && fixture.Motor &&
                fixture.Senses && fixture.Attack && fixture.Controller && fixture.Role != null,
                "Cannot prepare incomplete native combat fixture.");
            if (actorQuestOwned && !fixture.Root.GetComponent<QuestResourceBehaviour>())
                fixture.Root.AddComponent<QuestResourceBehaviour>();
            fixture.Senses.Target = target;
            fixture.Senses.enabled = false;
            fixture.Attack.enabled = false;
            fixture.Motor.enabled = false;
            fixture.Motor.IsHostile = false;
            fixture.Controller.enabled = true;
            fixture.Root.transform.position = PositionForController(platform, fixture.Controller);
            if (relocateTarget) target.transform.position = fixture.Root.transform.position + Vector3.right * 3f;
            Physics.SyncTransforms();
            if (target.EntityType != EntityTypes.Player)
            {
                fixture.Motor.MakeEnemyHostileToAttacker(target);
                // MakeEnemyHostileToAttacker only seeds pursuit when the existing target
                // is unseen or more than two metres away. In this controlled fixture the
                // disabled senses can retain a stale in-sight/near reading, so establish
                // the same native pursuit evidence explicitly for every non-player case.
                fixture.Motor.GiveUpTimer = 200;
                fixture.Senses.LastKnownTargetPos = target.transform.position;
                fixture.Senses.OldLastKnownTargetPos = target.transform.position;
                fixture.Senses.PredictedTargetPos = target.transform.position;
                Require(fixture.Motor.GiveUpTimer > 0 &&
                    fixture.Senses.PredictedTargetPos != EnemySenses.ResetPlayerPos,
                    "Controlled non-player target lacks native pursuit evidence.");
            }

            Func<DaggerfallEntityBehaviour, string> targetIdentity = candidate =>
                candidate == target ? targetId : null;
            fixture.Binding = NativeNpcCombatRoleBinding.Bind(fixture.Behaviour, fixture.Role,
                worldSeed, fixture.ActorId, targetIdentity, wallWaypointResolver);
            Require(fixture.Binding != null, "Native combat role binding rejected fixture role=" +
                fixture.Role.CombatRole + "; mobileID=" + (fixture.Behaviour.Entity as EnemyEntity).MobileEnemy.ID + ".");
            Require(fixture.Binding.OwnsTakeActionHandler,
                "Native role adapter did not wrap the native EnemyMotor.TakeAction callback.");
            fixture.Motor.enabled = true;
            fixture.InitialHandlerDescription = DescribeDelegate(fixture.Motor.TakeActionHandler, fixture.Motor);
            fixture.InitialRootInstanceId = fixture.Root.GetInstanceID();
            fixture.InitialBindingEnabled = fixture.Binding.enabled;
            fixture.InitialBindingOwnsHandler = fixture.Binding.OwnsTakeActionHandler;
            fixture.InitialMotorEnabled = fixture.Motor.enabled;
            fixture.InitialMobileId = ((EnemyEntity)fixture.Behaviour.Entity).MobileEnemy.ID;
            fixture.InitialLoadId = fixture.Native.LoadID;
            fixture.InitialHealth = fixture.Behaviour.Entity.CurrentHealth;
            fixture.InitialMaxHealth = fixture.Behaviour.Entity.MaxHealth;
            fixture.StartPosition = fixture.Root.transform.position;
            fixture.StartThreatGap = Vector3.Distance(fixture.StartPosition, target.transform.position);
        }

        private static DaggerfallEntityBehaviour PrepareInertTarget(GameObject targetObject,
            Vector3 position, float floorY)
        {
            var target = targetObject.GetComponent<DaggerfallEntityBehaviour>();
            var motor = targetObject.GetComponent<EnemyMotor>();
            var senses = targetObject.GetComponent<EnemySenses>();
            var attack = targetObject.GetComponent<EnemyAttack>();
            var controller = targetObject.GetComponent<CharacterController>();
            Require(target && motor && senses && attack && controller && target.Entity != null,
                "Native target fixture missed required enemy components.");
            motor.enabled = false;
            senses.enabled = false;
            attack.enabled = false;
            controller.enabled = true;
            targetObject.transform.position = PositionForController(new Vector3(position.x, floorY, position.z), controller);
            Physics.SyncTransforms();
            return target;
        }

        private static Vector3 PositionForController(Vector3 floorPoint, CharacterController controller)
        {
            Vector3 position = floorPoint;
            float bottomOffset = controller.center.y - controller.height * 0.5f;
            position.y -= bottomOffset;
            return position;
        }

        private static GameObject SpawnEnemy(MobileTypes mobileType, Vector3 position, string name)
        {
            UnityEngine.Random.State randomState = UnityEngine.Random.state;
            uint dfRandomSeed = DFRandom.Seed;
            GameObject actor = null;
            try
            {
                GameObject[] actors = GameObjectHelper.CreateFoeGameObjects(position, mobileType, 1, MobileReactions.Passive);
                Require(actors != null && actors.Length == 1 && actors[0],
                    "DFU did not spawn one native enemy fixture for " + mobileType + ".");
                actor = actors[0];
                DaggerfallEnemy enemy = actor.GetComponent<DaggerfallEnemy>();
                Require(enemy, "DFU native fixture has no DaggerfallEnemy persistence component.");
                // CreateFoeGameObjects assigns NextUID even to its inactive disposable actors.
                // Clear it before activation so serialization and host discovery cannot adopt it.
                enemy.LoadID = 0;
                actor.name = name;
                actor.hideFlags = HideFlags.HideAndDontSave;
                actor.SetActive(true);
                return actor;
            }
            catch
            {
                if (actor) UnityEngine.Object.DestroyImmediate(actor);
                throw;
            }
            finally
            {
                UnityEngine.Random.state = randomState;
                DFRandom.Seed = dfRandomSeed;
            }
        }

        private static MobileTypes MobileTypeForCareer(int careerIndex)
        {
            return (MobileTypes)(128 + careerIndex);
        }

        private static NpcRoleState Role(string personId, NpcCombatRole combatRole,
            NpcServiceBinding serviceBinding)
        {
            return new NpcRoleState
            {
                PersonId = personId,
                SocialRank = NpcSocialRank.Unknown,
                CombatRole = combatRole,
                CombatSourceId = "native:controlled-combat-role-probe",
                ServiceBinding = serviceBinding,
                ServiceSourceId = serviceBinding == NpcServiceBinding.Unknown ? null :
                    serviceBinding == NpcServiceBinding.NobleKingdom ? "native:controlled-palace-stationing" :
                    "user-policy:unbound-native-career",
                Revision = 1
            };
        }

        private static string FindEligibleActorId(uint worldSeed, string stem,
            DaggerfallEntityBehaviour target, string targetId, int exclusiveRollLimit)
        {
            for (int i = 0; i < 10000; i++)
            {
                string candidate = ProbeIdentityPrefix + stem + ":" + i.ToString("D4",
                    System.Globalization.CultureInfo.InvariantCulture);
                int roll;
                if (NativeNpcCombatRoleDecision.TryGetEncounterRoll(worldSeed, 0, candidate,
                    TargetLoadId(target), targetId, out roll) && roll < exclusiveRollLimit)
                    return candidate;
            }
            throw new InvalidOperationException("Could not find a bounded deterministic fixture ID for eligible roll < " +
                exclusiveRollLimit + ".");
        }

        private static string FindActorIdForRoll(uint worldSeed, string stem,
            DaggerfallEntityBehaviour target, string targetId, int wantedRoll)
        {
            for (int i = 0; i < 10000; i++)
            {
                string candidate = ProbeIdentityPrefix + stem + ":" + i.ToString("D4",
                    System.Globalization.CultureInfo.InvariantCulture);
                int roll;
                if (NativeNpcCombatRoleDecision.TryGetEncounterRoll(worldSeed, 0, candidate,
                    TargetLoadId(target), targetId, out roll) && roll == wantedRoll)
                    return candidate;
            }
            throw new InvalidOperationException("Could not find deterministic fixture identity for roll=" + wantedRoll + ".");
        }

        private static int EncounterRoll(uint worldSeed, NativeFixture fixture,
            DaggerfallEntityBehaviour target, string targetId)
        {
            int roll;
            if (!NativeNpcCombatRoleDecision.TryGetEncounterRoll(worldSeed, fixture.Native.LoadID,
                fixture.ActorId, TargetLoadId(target), targetId, out roll)) return -1;
            return roll;
        }

        private static ulong TargetLoadId(DaggerfallEntityBehaviour target)
        {
            DaggerfallEnemy native = target ? target.GetComponent<DaggerfallEnemy>() : null;
            return native ? native.LoadID : 0;
        }

        private static void DestroyOwned(GameObject actor, List<GameObject> owned)
        {
            if (!actor) return;
            actor.SetActive(false);
            UnityEngine.Object.DestroyImmediate(actor);
            owned.Remove(actor);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static string DescribeCallbackState(NativeFixture fixture)
        {
            if (fixture == null) return "callback-state={fixture=null}.";
            GameObject root = fixture.Root;
            DaggerfallEntityBehaviour behaviour = fixture.Behaviour;
            DaggerfallEnemy native = fixture.Native;
            EnemyMotor motor = fixture.Motor;
            EnemySenses senses = fixture.Senses;
            NativeNpcCombatRoleBinding binding = fixture.Binding;
            System.Delegate handler = motor ? motor.TakeActionHandler : null;
            bool entityAvailable = behaviour && behaviour.Entity != null;
            return "callback-state={rootRef=" + ObjectReference(root) +
                "; rootAlive=" + (root != null) +
                "; rootActiveSelf=" + (root ? root.activeSelf.ToString() : "n/a") +
                "; rootActiveHierarchy=" + (root ? root.activeInHierarchy.ToString() : "n/a") +
                "; rootId=" + (root ? root.GetInstanceID().ToString() : "n/a") +
                "; initialRootId=" + fixture.InitialRootInstanceId +
                "; behaviourAlive=" + (behaviour != null) +
                "; entityType=" + (entityAvailable ? behaviour.EntityType.ToString() : "n/a") +
                "; health=" + (entityAvailable ? behaviour.Entity.CurrentHealth.ToString() : "n/a") +
                "; maxHealth=" + (entityAvailable ? behaviour.Entity.MaxHealth.ToString() : "n/a") +
                "; initialHealth=" + fixture.InitialHealth + "; initialMaxHealth=" + fixture.InitialMaxHealth +
                "; nativeAlive=" + (native != null) +
                "; loadId=" + (native ? native.LoadID.ToString() : "n/a") +
                "; mobileId=" + (entityAvailable && behaviour.Entity is EnemyEntity ?
                    ((EnemyEntity)behaviour.Entity).MobileEnemy.ID.ToString() : "n/a") +
                "; initialLoadId=" + fixture.InitialLoadId + "; initialMobileId=" + fixture.InitialMobileId +
                "; motorAlive=" + (motor != null) +
                "; motorEnabled=" + (motor ? motor.enabled.ToString() : "n/a") +
                "; motorActive=" + (motor ? motor.isActiveAndEnabled.ToString() : "n/a") +
                "; canAct=" + (motor ? motor.CanAct.ToString() : "n/a") +
                "; isHostile=" + (motor ? motor.IsHostile.ToString() : "n/a") +
                "; giveUpTimer=" + (motor ? motor.GiveUpTimer.ToString() : "n/a") +
                "; bindingRef=" + ObjectReference(binding) +
                "; bindingAlive=" + (binding != null) +
                "; bindingEnabled=" + (binding ? binding.enabled.ToString() : "n/a") +
                "; bindingActive=" + (binding ? binding.isActiveAndEnabled.ToString() : "n/a") +
                "; initialBindingEnabled=" + fixture.InitialBindingEnabled +
                "; initialBindingOwns=" + fixture.InitialBindingOwnsHandler +
                "; initialMotorEnabled=" + fixture.InitialMotorEnabled +
                "; bindingOwns=" + (binding ? binding.OwnsTakeActionHandler.ToString() : "n/a") +
                "; bindingActorMatches=" + ReadBindingReferenceMatch(binding, "actor", behaviour) +
                "; bindingMotorMatches=" + ReadBindingReferenceMatch(binding, "motor", motor) +
                "; ownershipLost=" + ReadBindingFlag(binding, "ownershipLost") +
                "; configured=" + ReadBindingFlag(binding, "configured") +
                "; installedDelegate=" + DescribeBindingDelegate(binding, "installedTakeAction", motor) +
                "; originalDelegate=" + DescribeBindingDelegate(binding, "originalTakeAction", motor) +
                "; currentHandler=" + DescribeDelegate(handler, motor) +
                "; initialHandler=" + (fixture.InitialHandlerDescription ?? "n/a") +
                "; sensesAlive=" + (senses != null) +
                "; sensesEnabled=" + (senses ? senses.enabled.ToString() : "n/a") +
                "; sensesTarget=" + (senses && senses.Target ? senses.Target.name : "null") +
                "; detectedTarget=" + (senses ? senses.DetectedTarget.ToString() : "n/a") +
                "; components=" + DescribeComponents(root) + "}.";
        }

        private static string ObjectReference(UnityEngine.Object value)
        {
            if (object.ReferenceEquals(value, null)) return "clr-null";
            return value == null ? "unity-destroyed" : value.GetType().FullName + "#" + value.GetInstanceID();
        }

        private static string ReadBindingFlag(NativeNpcCombatRoleBinding binding, string fieldName)
        {
            if (!binding) return "n/a";
            var field = typeof(NativeNpcCombatRoleBinding).GetField(fieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field == null) return "field-missing";
            object value = field.GetValue(binding);
            return value == null ? "null" : value.ToString();
        }

        private static string ReadBindingReferenceMatch(NativeNpcCombatRoleBinding binding,
            string fieldName, object expected)
        {
            if (!binding) return "n/a";
            var field = typeof(NativeNpcCombatRoleBinding).GetField(fieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field == null) return "field-missing";
            object value = field.GetValue(binding);
            return object.ReferenceEquals(value, expected).ToString();
        }

        private static string DescribeBindingDelegate(NativeNpcCombatRoleBinding binding,
            string fieldName, EnemyMotor expectedOwner)
        {
            if (!binding) return "n/a";
            var field = typeof(NativeNpcCombatRoleBinding).GetField(fieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return field == null ? "field-missing" : DescribeDelegate(field.GetValue(binding) as System.Delegate,
                expectedOwner);
        }

        private static string DescribeDelegate(System.Delegate callback, EnemyMotor expectedOwner)
        {
            if (callback == null) return "null";
            System.Delegate[] invocationList = callback.GetInvocationList();
            string result = string.Empty;
            for (int i = 0; i < invocationList.Length; i++)
            {
                if (i > 0) result += "|";
                System.Delegate item = invocationList[i];
                object target = item.Target;
                result += (item.Method.DeclaringType == null ? "?" : item.Method.DeclaringType.FullName) + "." +
                    item.Method.Name + "; target=" + (target == null ? "static" : target.GetType().FullName) +
                    "; sameMotor=" + object.ReferenceEquals(target, expectedOwner);
            }
            return result;
        }

        private static string DescribeComponents(GameObject root)
        {
            if (!root) return "<destroyed>";
            Component[] components = root.GetComponentsInChildren<Component>(true);
            string result = string.Empty;
            for (int i = 0; i < components.Length; i++)
            {
                if (i > 0) result += ",";
                Component component = components[i];
                if (!component) { result += "<missing>"; continue; }
                result += component.gameObject.name + ":" + component.GetType().FullName;
                Behaviour behaviour = component as Behaviour;
                if (behaviour) result += "[enabled=" + behaviour.enabled + "]";
            }
            return result;
        }
    }
}
