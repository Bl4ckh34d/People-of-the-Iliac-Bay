using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>Opt-in native action fixture. The caller supplies an actual equipped managed fighter.</summary>
    public static class NativeNpcCombatAssistProbe
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        public static IEnumerator RunFixture(PeopleOfTheIliacBayMod host, StreetPopulation streets, string expectedCharacter)
        {
            var game = GameManager.Instance;
            string save = CommandLineValue("-peopleoftheiliacbay-fixture-save");
            Require(OptedIn() && host && streets && game != null && game.PlayerEntity != null &&
                NativeProbeText.IsTestCharacter(expectedCharacter ?? "") &&
                game.PlayerEntity.Name == expectedCharacter && !string.IsNullOrWhiteSpace(save),
                "Combat fixture requires its explicit opt-in, exact owned disposable character and -peopleoftheiliacbay-fixture-save.");
            game.SaveLoadManager.EnumerateSaves();
            Require(game.SaveLoadManager.FindSaveFolderByNames(expectedCharacter, save) >= 0,
                "Combat fixture baseline save is missing.");
            string failure = null;
            var pending = new Stack<IEnumerator>();
            try
            {
            pending.Push(DiscoverAndVerify(host, streets, expectedCharacter));
            // Drive nested checks here so their failures cannot bypass the exact
            // baseline restore by escaping through Unity's nested coroutine runner.
            while (pending.Count > 0)
            {
                bool more;
                object yielded = null;
                try { more = pending.Peek().MoveNext(); if (more) yielded = pending.Peek().Current; }
                catch (Exception error) { failure = error.ToString(); break; }
                if (!more)
                {
                    try { (pending.Pop() as IDisposable)?.Dispose(); }
                    catch (Exception error) { failure = "Combat fixture cleanup failed: " + error; break; }
                    continue;
                }
                var nested = yielded as IEnumerator;
                if (nested != null) { pending.Push(nested); continue; }
                yield return yielded;
            }
            while (pending.Count > 0)
                try { (pending.Pop() as IDisposable)?.Dispose(); }
                catch (Exception error) { failure = (failure == null ? "" : failure + " | ") + "Combat fixture cleanup failed: " + error; }
            TownSmokeHarness.Watch(null);
            DaggerfallUI.Instance.PopToHUD(); game.PauseGame(false);
            yield return ConversationMoodProbe.LoadSave(host, streets, expectedCharacter, save, false);
            Require(streets.Ready, "Combat baseline did not reload to a ready population.");
            if (failure != null) throw new InvalidOperationException("Combat probe failed before baseline restore: " + failure);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMBAT_ASSIST_FIXTURE_PASS: existingGeneratedAdventurer=true; " +
                "noCitizenPromotion=true; actualOwnedWeapon=true; exactBaselineRestored=true.");
            }
            finally { TownSmokeHarness.Watch(null); }
        }

        private static IEnumerator DiscoverAndVerify(PeopleOfTheIliacBayMod host, StreetPopulation streets, string expectedCharacter)
        {
            var store = typeof(PeopleOfTheIliacBayMod).GetField("store", Private).GetValue(host) as WorldStateStore;
            Require(store != null && streets.Ready, "Combat discovery needs the ready caller-owned population.");
            int generated = 0, visible = 0, armed = 0;
            ResidentActor selected = null;
            foreach (var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
            {
                if (!actor || actor.Resident == null || !actor.Resident.IsAlive ||
                    actor.Resident.IsQuestProtected || !actor.gameObject.activeInHierarchy || actor.IsSleeping ||
                    actor.IsConversing || store.Traveler(actor.Resident.Id) == null ||
                    !TravelerAdventurerPolicy.IsAdventurer(store.ResolveNpcArchetype(actor.Resident.Id))) continue;
                generated++;
                var scene = DefenseScene.For(actor);
                if (scene == null || !scene.PlayerPresent ||
                    !CivilianResponses.Sees(GameManager.Instance.PlayerMotor.transform.position, actor.transform.position, 20)) continue;
                visible++;
                DaggerfallUnityItem weapon;
                if (!NativeNpcCombatAssist.TryWeapon(actor, out weapon)) continue;
                armed++;
                PeopleOfTheIliacBayMod.RefreshSavedNpcRole(store, actor.Resident.Id);
                var role = store.ReadNpcRole(actor.Resident.Id);
                float gap = Vector3.Distance(actor.transform.position, GameManager.Instance.PlayerMotor.transform.position);
                if (!NpcRolePolicy.IsCombatExperienced(role) || role.ServiceBinding != NpcServiceBinding.Independent ||
                    !streets.HasBoundTravelerActionPlan(actor.Resident.Id) || gap <= 2.5f || gap >= 7 ||
                    streets.IsAlarmed(actor.Resident.Id) || store.ActiveNpcAction(actor.Resident.Id) != null ||
                    store.ActiveNpcTask(actor.Resident.Id) != null) continue;
                selected = actor; break;
            }
            Require(selected, "No idle visible equipped generated adventurer is available 2.5–7 metres from the player. " +
                "generated=" + generated + "; visible=" + visible + "; armed=" + armed +
                ". Approach an existing awake independent traveler; this fixture does not create or relabel one.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMBAT_ASSIST_ACTOR: id=" + selected.Resident.Id +
                "; archetype=" + store.ResolveNpcArchetype(selected.Resident.Id) + "; generatedTraveler=true.");
            yield return Verify(streets, store, selected, expectedCharacter);
        }

        public static IEnumerator Verify(StreetPopulation streets, WorldStateStore store,
            ResidentActor actor, string expectedCharacter)
        {
            bool enabled = OptedIn();
            Require(enabled && expectedCharacter != null && expectedCharacter.StartsWith("PeopleOfTheIliacBayTest-", StringComparison.Ordinal) &&
                GameManager.Instance.PlayerEntity.Name == expectedCharacter,
                "Combat probe requires its opt-in flag and caller-owned disposable character.");
            DaggerfallUnityItem weapon = null;
            Require(streets && streets.Ready && store != null && actor && actor.Resident != null &&
                NativeNpcCombatAssist.TryWeapon(actor, out weapon),
                "Combat probe needs an actual equipped managed fighter with native weapon skill.");
            var role = store.ReadNpcRole(actor.Resident.Id);
            Require(NpcRolePolicy.IsCombatExperienced(role) && role.ServiceBinding == NpcServiceBinding.Independent,
                "Combat probe does not promote a citizen or borrow a kingdom guard.");
            Require(store.ActiveNpcAction(actor.Resident.Id) == null && store.ActiveNpcTask(actor.Resident.Id) == null &&
                Vector3.Distance(actor.transform.position, GameManager.Instance.PlayerMotor.transform.position) > 2.5f &&
                Vector3.Distance(actor.transform.position, GameManager.Instance.PlayerMotor.transform.position) < 7,
                "Combat fixture requires an idle fighter 2.5–7 metres from the nearby player and no existing task.");
            var game = GameManager.Instance;
            int health = game.PlayerEntity.CurrentHealth;
            bool godMode = game.PlayerEntity.GodMode;
            var guards = streets.GetComponent<GuardPopulation>();
            bool guardDefense = guards && guards.EnemyDefense;
            bool infighting = DaggerfallUnity.Settings.EnemyInfighting;
            var actorBody = actor.GetComponent<DaggerfallEntityBehaviour>();
            int actorHealth = actorBody.Entity.CurrentHealth;
            GameObject first = null, second = null;
            try
            {
                TownSmokeHarness.Watch(actor.transform);
                // Isolate attribution: an unrelated guard or native infighter must
                // not satisfy this actual resident's physical hit observation.
                if (guards) guards.EnemyDefense = false;
                DaggerfallUnity.Settings.EnemyInfighting = false;
                game.PlayerEntity.GodMode = false;
                game.PlayerEntity.SetHealth(game.PlayerEntity.MaxHealth, true);
                first = Spawn(actor);
                yield return null; // Initialize the real native EnemyAttack component.
                var target = first.GetComponent<DaggerfallEntityBehaviour>();
                ObserveActualNativeAttack(target, store);
                target.Entity.MaxHealth = 500; target.Entity.SetHealth(500, true);
                string id = NativeNpcCombatAssist.Identity(target);
                Vector3 before = actor.transform.position;
                string actionId = Accept(streets, store, actor, id, NpcActionKind.Attack);
                int initial = target.Entity.CurrentHealth;
                int skill = weapon.GetWeaponSkillIDAsShort();
                var skillProgress = store.ReadNpcProgress(actor.Resident.Id);
                var skillRow = skillProgress == null ? null : skillProgress.Skills.Find(row => row.Skill == skill);
                long experienceBefore = skillRow == null ? 0 : skillRow.Experience;
                int nativeSkillBefore = actorBody.Entity.Skills.GetPermanentSkillValue(skill);
                float timeout = Time.realtimeSinceStartup + 50;
                while (target && target.Entity.CurrentHealth == initial && Time.realtimeSinceStartup < timeout)
                {
                    RequireRunning(store.ReadNpcAction(actionId), "Native fighter stopped before its first formula hit.");
                    yield return null;
                }
                Require(target && target.Entity.CurrentHealth < initial && target.Entity.CurrentHealth > 0,
                    "Actual equipped resident did not damage the native enemy through the native formula.");
                Require(Vector3.Distance(before, actor.transform.position) > 1,
                    "Combat probe did not observe physical route approach by the real fighter.");
                skillProgress = store.ReadNpcProgress(actor.Resident.Id);
                skillRow = skillProgress == null ? null : skillProgress.Skills.Find(row => row.Skill == skill);
                int derivedSkill = store.ReadNpcActor(actor.Resident.Id, store.ResolveNpcArchetype(actor.Resident.Id)).Skill(skill);
                int observedHealthLoss = initial - target.Entity.CurrentHealth;
                // Polling can observe more than one native swing. Every awarded hit must
                // remove at least one HP; CPU adapter tests verify the per-hit exact tally.
                Require(skillRow != null && skillRow.Experience > experienceBefore &&
                    skillRow.Experience - experienceBefore <= observedHealthLoss * NativeNpcCompanionExperience.ExperiencePerSuccessfulUse &&
                    actorBody.Entity.Skills.GetPermanentSkillValue(skill) == derivedSkill && derivedSkill >= nativeSkillBefore,
                    "Observed admitted adventurer hits did not award bounded weapon experience to ledger and native body.");
                NativeNpcCompanionCastProbe.VerifySkillPersistence(store, actor.Resident.Id, skill, skillRow.Experience, derivedSkill);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMBAT_ASSIST_SKILL_PASS: admittedAttack=true; generatedAdventurer=true; skill=" +
                    skill + "; rawExperience=" + experienceBefore + "->" + skillRow.Experience +
                    "; nativeSkill=" + nativeSkillBefore + "->" + derivedSkill + "; observedHealthLoss=" + observedHealthLoss +
                    "; persisted=true; rebuiltNativeSkills=true");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMBAT_ASSIST_HIT_PASS: actualManagedFighter=true; nativeFormula=true; hpBefore=" +
                    initial + "; hpAfter=" + target.Entity.CurrentHealth + "; target=" + id);
                target.Entity.SetHealth(1, true);
                timeout = Time.realtimeSinceStartup + 50;
                NpcActionJob job;
                do
                {
                    job = store.ReadNpcAction(actionId);
                    if (job != null && NpcActionExecutor.IsTerminal(job)) break;
                    yield return null;
                } while (Time.realtimeSinceStartup < timeout);
                job = store.ReadNpcAction(actionId);
                Require(job != null && job.Phase == NpcActionPhase.Completed &&
                    job.CompletedEffectId == job.Id + ":target-defeated" && store.GetDefenseCombatant(id).Death != null,
                    "Native target death did not complete the exact admitted fight.");

                second = Spawn(actor);
                yield return null;
                target = second.GetComponent<DaggerfallEntityBehaviour>();
                ObserveActualNativeAttack(target, store);
                target.Entity.MaxHealth = 500; target.Entity.SetHealth(500, true);
                id = NativeNpcCombatAssist.Identity(target);
                actionId = Accept(streets, store, actor, id, NpcActionKind.Defend);
                timeout = Time.realtimeSinceStartup + 5;
                do
                {
                    job = store.ReadNpcAction(actionId);
                    if (job != null && job.Phase == NpcActionPhase.Executing) break;
                    RequireRunning(job, "Exact defense stopped before observing the attacker.");
                    yield return null;
                } while (Time.realtimeSinceStartup < timeout);
                Require(job != null && job.Phase == NpcActionPhase.Executing, "Defender did not reach its nearby exact attacker.");
                UnityEngine.Object.Destroy(second); second = null;
                timeout = Time.realtimeSinceStartup + 5;
                do
                {
                    yield return null;
                    job = store.ReadNpcAction(actionId);
                    if (job != null && NpcActionExecutor.IsTerminal(job)) break;
                } while (Time.realtimeSinceStartup < timeout);
                Require(job != null && job.Phase == NpcActionPhase.Failed && job.CompletedEffectId == null &&
                    store.GetDefenseCombatant(id).Death == null,
                    "Disappearing native attacker incorrectly became a combat success.");
                second = Spawn(actor);
                yield return null;
                target = second.GetComponent<DaggerfallEntityBehaviour>();
                ObserveActualNativeAttack(target, store);
                target.Entity.MaxHealth = 500; target.Entity.SetHealth(500, true);
                id = NativeNpcCombatAssist.Identity(target);
                actionId = Accept(streets, store, actor, id, NpcActionKind.Defend);
                yield return VerifyInjuryResponse(store, actor, role, target, actionId);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMBAT_ASSIST_NATIVE_PASS: actualManagedFighter=true; actualOwnedWeapon=true; " +
                    "observedNativePlayerAttack=true; actualFormulaHit=true; targetDeathCompleted=true; disappearanceFailed=true; injuryRolePolicy=true.");
            }
            finally
            {
                TownSmokeHarness.Watch(null);
                string ignored;
                if (streets && actor) streets.TryStopConversationAction(actor.Resident.Id, out ignored);
                if (first) UnityEngine.Object.Destroy(first);
                if (second) UnityEngine.Object.Destroy(second);
                if (actorBody && actorBody.Entity.CurrentHealth > 0)
                { actorBody.Entity.SetHealth(actorHealth, true); var binding = actor.GetComponent<NativeNpcVitalsBinding>(); if (binding) binding.Capture(); }
                if (guards) guards.EnemyDefense = guardDefense;
                DaggerfallUnity.Settings.EnemyInfighting = infighting;
                game.PlayerEntity.GodMode = false;
                game.PlayerEntity.SetHealth(health, true);
                game.PlayerEntity.GodMode = godMode;
            }
        }

        private static IEnumerator VerifyInjuryResponse(WorldStateStore store, ResidentActor actor,
            NpcRoleState role, DaggerfallEntityBehaviour target, string actionId)
        {
            var body = actor.GetComponent<DaggerfallEntityBehaviour>();
            int injuredHealth = Math.Max(1, body.Entity.MaxHealth / 4);
            if (body.Entity.CurrentHealth > injuredHealth)
                body.DamageHealthFromSource(target, body.Entity.CurrentHealth - injuredHealth, false, actor.transform.position);
            var binding = actor.GetComponent<NativeNpcVitalsBinding>();
            if (binding) binding.Capture();
            float initialGap = Vector3.Distance(actor.transform.position, target.transform.position);
            int encounterRoll;
            Require(NativeNpcCombatRoleDecision.TryGetEncounterRoll(store.WorldSeed, 0, actor.Resident.Id,
                target.GetComponent<DaggerfallEnemy>().LoadID, NativeNpcCombatAssist.Identity(target), out encounterRoll),
                "Combat fixture could not resolve the stable retreat encounter.");
            bool expectedRetreat = NpcRolePolicy.ShouldRetreat(role, body.Entity.CurrentHealth, body.Entity.MaxHealth, encounterRoll);
            float timeout = Time.realtimeSinceStartup + 2;
            while (Time.realtimeSinceStartup < timeout) yield return null;
            var job = store.ReadNpcAction(actionId);
            Require(job != null && (!NpcActionExecutor.IsTerminal(job) || job.Phase == NpcActionPhase.Failed &&
                job.FailureReason == "The seriously wounded fighter withdrew from combat."),
                "Generic health care interrupted the admitted fighter before its role response: " + (job == null ? "missing" : job.FailureReason));
            if (expectedRetreat)
                Require(Vector3.Distance(actor.transform.position, target.transform.position) > initialGap + 0.2f,
                    "The injured independent fighter did not physically withdraw under its stable retreat decision.");
            else Require(!NpcActionExecutor.IsTerminal(job), "A fighter that chose to continue was removed by generic injury care.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_COMBAT_ASSIST_INJURY_PASS: rolePolicyOwned=true; expectedRetreat=" + expectedRetreat +
                "; observedGap=" + Vector3.Distance(actor.transform.position, target.transform.position));
        }

        private static bool OptedIn()
        { return Environment.GetEnvironmentVariable("POTIB_COMBAT_ASSIST") == "1" &&
            Environment.GetEnvironmentVariable("POTIB_ROADMAP_PROBE") == "combat-assist"; }

        private static string CommandLineValue(string name)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++) if (args[i] == name) return args[i + 1];
            return null;
        }

        private static GameObject Spawn(ResidentActor actor)
        {
            var scene = DefenseScene.For(actor);
            Require(scene != null && scene.PlayerPresent, "Combat fixture needs a loaded settlement scene.");
            var player = GameManager.Instance.PlayerMotor.transform.position;
            var direction = player - actor.transform.position;
            direction.y = 0;
            if (direction.sqrMagnitude < 0.01f) direction = actor.transform.forward;
            direction.Normalize();
            var position = player + new Vector3(direction.z, 0, -direction.x) * 1.2f;
            var enemy = GameObjectHelper.CreateFoeGameObjects(position, MobileTypes.Rat, 1, MobileReactions.Hostile)[0];
            enemy.transform.SetParent(scene.Root, true); enemy.SetActive(true);
            return enemy;
        }

        private static void ObserveActualNativeAttack(DaggerfallEntityBehaviour attacker, WorldStateStore store)
        {
            var motor = attacker.GetComponent<EnemyMotor>();
            var senses = attacker.GetComponent<EnemySenses>();
            var attack = attacker.GetComponent<EnemyAttack>();
            motor.enabled = false; motor.IsHostile = true;
            senses.enabled = false; senses.Target = GameManager.Instance.PlayerEntityBehaviour;
            attack.enabled = false;
            int before = GameManager.Instance.PlayerEntity.CurrentHealth;
            var method = typeof(EnemyAttack).GetMethod("ApplyDamageToPlayer", Private);
            for (int i = 0; i < 256 && GameManager.Instance.PlayerEntity.CurrentHealth == before; i++)
                method.Invoke(attack, new object[] { null });
            var record = store.GetDefenseCombatant(NativeNpcCombatAssist.Identity(attacker));
            Require(GameManager.Instance.PlayerEntity.CurrentHealth < before && record != null &&
                record.UnprovokedAttackAt >= 0 && !record.PlayerProvoked,
                "Actual native attack did not produce physical player damage and aggression evidence.");
        }

        private static string Accept(StreetPopulation streets, WorldStateStore store, ResidentActor actor,
            string targetId, NpcActionKind kind)
        {
            string request = kind == NpcActionKind.Attack ? "Fight that attacker." : "Defend me against that attacker.";
            var option = streets.ConversationActionCandidates(actor.Resident.Id).Find(candidate =>
                candidate.Kind == kind && candidate.Target != null && candidate.Target.PersonId == targetId);
            Require(option != null, "No real exact native attacker was offered for " + kind + ".");
            string reason;
            Require(NpcProbeCommandSource.TryAcceptAction(streets, actor.Resident.Id,
                "combat-assist-probe:" + Guid.NewGuid().ToString("N"),
                new NpcActionDecision { Option = option, DurationSeconds = 900 }, request, out reason),
                "Native combat action acceptance failed: " + reason);
            var job = store.ActiveNpcAction(actor.Resident.Id);
            Require(job != null && job.Kind == kind && job.Target.PersonId == targetId,
                "Accepted combat action lost exact target identity.");
            return job.Id;
        }

        private static void RequireRunning(NpcActionJob job, string reason)
        { Require(job != null && !NpcActionExecutor.IsTerminal(job), reason + (job == null ? "" : " " + job.FailureReason)); }
        private static void Require(bool condition, string reason)
        { if (!condition) throw new InvalidOperationException(reason); }
    }
}
