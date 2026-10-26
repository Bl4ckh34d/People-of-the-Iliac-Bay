using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Guilds;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Game.UserInterface;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Opt-in acceptance only. It consumes a real loaded temple, trainer and
    // resident. Its owned disposable save grants one existing resident the
    // exact native lesson price through the normal inventory binding; it never
    // creates an actor, item, income source, or production wallet credit.
    internal static class NativeNpcTempleTrainingProbe
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        internal static IEnumerator RunFixture(PeopleOfTheIliacBayMod host, StreetPopulation streets, string character)
        {
            Require(Array.IndexOf(Environment.GetCommandLineArgs(), "-peopleoftheiliacbay-temple-training-probe") >= 0 &&
                character == "PeopleOfTheIliacBayTest-9a5f197b", "Temple training probe requires its explicit owned fixture.");
            var game = GameManager.Instance; game.SaveLoadManager.EnumerateSaves();
            int slot = game.SaveLoadManager.FindSaveFolderByNames(character, "People - Gothway Garden");
            Require(slot >= 0, "Owned Gothway Garden fixture is missing.");
            bool loaded = false; SaveLoadManager.OnLoadEventHandler onLoad = data => loaded = true; SaveLoadManager.OnLoad += onLoad;
            try
            {
                game.SaveLoadManager.Load(slot); DaggerfallUI.Instance.PopToHUD();
                float until = Time.realtimeSinceStartup + 90;
                while ((!loaded || !streets.Ready || game.PlayerEnterExit.IsPlayerInside) && Time.realtimeSinceStartup < until) yield return null;
                Require(loaded && streets.Ready && !game.PlayerEnterExit.IsPlayerInside && game.PlayerEntity.Name == character,
                    "Owned temple fixture did not load the exterior town.");
            }
            finally { SaveLoadManager.OnLoad -= onLoad; }
            var directory = game.StreamingWorld.GetCurrentBuildingDirectory(); Require(directory, "Temple fixture has no building directory.");
            DaggerfallStaticDoors owner = null; StaticDoor entry = default(StaticDoor);
            foreach (var doors in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
            foreach (var door in doors.Doors)
            {
                BuildingSummary summary;
                if (door.doorType == DoorTypes.Building && directory.GetBuildingSummary(door.buildingKey, out summary) &&
                    summary.BuildingType == DFLocation.BuildingTypes.Temple && PlayerActivate.IsBuildingOpen(summary.BuildingType))
                { owner = doors; entry = door; break; }
            }
            Require(owner, "No currently open real temple door exists in the owned fixture.");
            var store = (WorldStateStore)typeof(StreetPopulation).GetField("worldStore", Private).GetValue(streets);
            Require(store != null, "Temple probe has no live world store.");
            ResidentActor trainee = null; NpcActionOption enter = null; string diagnostic = null, loggedDiagnostic = null;
            float candidateDeadline = Time.realtimeSinceStartup + 45;
            while (Time.realtimeSinceStartup < candidateDeadline && !TryFindEligibleTempleEntry(streets, store, entry.buildingKey,
                out trainee, out enter, out diagnostic))
            {
                if (!string.IsNullOrEmpty(diagnostic) && diagnostic != loggedDiagnostic)
                { Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_TEMPLE_TRAINING_PROBE_CANDIDATE: " + diagnostic); loggedDiagnostic = diagnostic; }
                yield return new WaitForSecondsRealtime(.25f);
            }
            Require(trainee && enter != null, "No existing eligible resident can enter this real temple: " + diagnostic);
            string traineeId = trainee.Resident.Id;
            int controlledBudget; string budgetReason;
            Require(TrySetControlledFixtureBudget(store, trainee, out controlledBudget, out budgetReason),
                "Temple probe could not apply its controlled disposable NPC budget: " + budgetReason);
            Require(StagePlayerNearExistingActor(game, trainee), "Temple probe could not stage the player near the selected existing resident.");
            string acceptReason;
            Require(NpcProbeCommandSource.TryAcceptAction(streets, traineeId, "native-temple-entry:" + Guid.NewGuid().ToString("N"),
                new NpcActionDecision { Option = enter, DurationSeconds = 300 }, out acceptReason),
                "Native temple entry action was not accepted: " + acceptReason);
            var entered = store.ActiveNpcAction(traineeId);
            Require(entered != null && entered.Kind == NpcActionKind.Enter && entered.Target != null &&
                entered.Target.BuildingKey == entry.buildingKey, "Selected resident lost the exact native temple doorway.");
            float deadline = Time.realtimeSinceStartup + 120;
            while (Time.realtimeSinceStartup < deadline)
            {
                entered = store.ReadNpcAction(entered.Id);
                if (entered == null || NpcActionExecutor.IsTerminal(entered)) break;
                yield return null;
            }
            Require(entered != null && entered.Phase == NpcActionPhase.Completed && entered.BuildingTransitionReceipt != null &&
                entered.BuildingTransitionReceipt.BuildingKey == entry.buildingKey && !game.PlayerEnterExit.IsPlayerInside &&
                !streets.FindActor(traineeId), "Selected resident did not physically cross the exact temple door.");
            TownSmokeHarness.EnterInterior(owner.transform, entry); DaggerfallUI.Instance.PopToHUD();
            deadline = Time.realtimeSinceStartup + 90;
            while ((!game.PlayerEnterExit.Interior || !streets.Ready || !InteriorAdmissionReady(streets) || !streets.FindActor(traineeId)) &&
                Time.realtimeSinceStartup < deadline) yield return null;
            var room = game.PlayerEnterExit.Interior;
            trainee = streets.FindActor(traineeId);
            Require(room && room.EntryDoor.buildingKey == entry.buildingKey && room.BuildingData.BuildingType == DFLocation.BuildingTypes.Temple &&
                trainee && trainee.gameObject.activeInHierarchy && store.ReadNpcInventory(traineeId).WalletGold == controlledBudget,
                "Native temple entry did not materialize the same funded resident in the selected public building.");
            var keeper = typeof(StreetPopulation).GetMethod("KeeperPerson", Private);
            Require(keeper != null, "Temple probe cannot resolve the real native trainer.");
            Func<StaticNPC, NativePerson> resolve = npc => (NativePerson)keeper.Invoke(streets, new object[] { npc });
            NpcActionOption choice = null; diagnostic = null; loggedDiagnostic = null;
            float admissionDeadline = Time.realtimeSinceStartup + 45;
            while (Time.realtimeSinceStartup < admissionDeadline && !TryFindCurrentOffer(streets, room, store, resolve, traineeId, out trainee, out choice, out diagnostic))
            {
                if (!string.IsNullOrEmpty(diagnostic) && diagnostic != loggedDiagnostic)
                { Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_TEMPLE_TRAINING_PROBE_WAIT: " + diagnostic); loggedDiagnostic = diagnostic; }
                yield return new WaitForSecondsRealtime(.25f);
            }
            Require(trainee && choice != null, "No existing eligible resident and current public temple trainer offered native training: " + diagnostic);
            int skill; Require(int.TryParse(choice.Target.ItemId, out skill), "Temple offer omitted its native skill.");
            StaticNPC trainer = null; NativeNpcTempleTrainingOffer offer = null;
            foreach (var candidate in room.GetComponentsInChildren<StaticNPC>())
            {
                NativeNpcTempleTrainingOffer proposed; string rejected;
                if (NativeNpcTempleTraining.TryPrepareOffer(store, trainee, candidate, resolve, room.BuildingData.FactionId, skill, out proposed, out rejected) &&
                    proposed.Trainer.Id == choice.Target.PersonId) { trainer = candidate; offer = proposed; break; }
            }
            Require(trainer && offer != null && offer.Price == choice.Target.QuotedGold, "Offered trainer, eligibility, or native non-member price changed.");
            Require(StagePlayerNearExistingActor(game, trainee), "Temple probe could not stage the player near the existing resident.");
            var inventoryOwner = trainee.GetComponent<NativeNpcInventoryOwner>();
            Require(inventoryOwner && inventoryOwner.Binding != null && inventoryOwner.Binding.PersonId == trainee.Resident.Id,
                "Training resident has no matching native inventory identity.");
            int npcGold = store.ReadNpcInventory(trainee.Resident.Id).WalletGold;
            int playerGold = game.PlayerEntity.GetGoldAmount(); short[] playerSkills = PlayerSkills(game);
            var beforeActor = store.ReadNpcActor(trainee.Resident.Id, store.ResolveNpcArchetype(trainee.Resident.Id));
            int beforeSkill = beforeActor.Skill(skill); int trainerHash = trainer.Data.hash;
            long beforeExperience = Experience(store.ReadNpcProgress(traineeId), skill);
            var traineeBody = trainee.GetComponent<DaggerfallEntityBehaviour>(); Require(traineeBody && traineeBody.Entity != null, "Training resident has no native skill body.");
            short beforeNativeSkill = traineeBody.Entity.Skills.GetPermanentSkillValue((DFCareer.Skills)skill);
            int nativeNpcLevel = traineeBody.Entity.Level;
            Require(nativeNpcLevel == 1 && offer.Price == Guild.nonMemberTrainingCost * nativeNpcLevel,
                "Temple offer did not retain native non-member cost at the resident's actual native level.");
            Require(npcGold == offer.Price && controlledBudget == offer.Price,
                "Controlled fixture budget was not the actual native lesson price.");
            double playerTimeBeforeStart = Now();
            Require(NpcProbeCommandSource.TryAcceptAction(streets, traineeId, "native-temple-training:" + Guid.NewGuid().ToString("N"),
                new NpcActionDecision { Option = choice, DurationSeconds = (int)NativeNpcTempleTraining.DurationSeconds + 600 }, out acceptReason),
                "Native temple training action was not accepted: " + acceptReason);
            var accepted = store.ActiveNpcAction(traineeId);
            Require(accepted != null && accepted.ServiceCode == NativeNpcTempleTraining.ServiceCode && accepted.Target.PersonId == offer.Trainer.Id &&
                accepted.Target.ItemId == choice.Target.ItemId,
                "Accepted training action lost the exact native trainer or selected skill.");
            deadline = Time.realtimeSinceStartup + 120; NpcActionJob running = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                running = store.ReadNpcAction(accepted.Id);
                if (running == null || NpcActionExecutor.IsTerminal(running) || running.ServiceStep == 1) break;
                yield return null;
            }
            Require(running != null && running.Phase == NpcActionPhase.Executing && running.ServiceStep == 1 && running.FinishAt > 0,
                "Native lesson did not reach its paid running stage: " + (running == null ? "missing" : running.Phase + " " + running.FailureReason));
            double playerTimeAtRunning = Now();
            Require(playerTimeAtRunning >= playerTimeBeforeStart && playerTimeAtRunning - playerTimeBeforeStart < 120d &&
                running.FinishAt - playerTimeAtRunning > NativeNpcTempleTraining.DurationSeconds - 120d &&
                running.FinishAt - playerTimeAtRunning <= NativeNpcTempleTraining.DurationSeconds + 1d,
                "Starting training advanced global time instead of arming a three-hour NPC timer.");
            Require(store.ReadNpcInventory(traineeId).WalletGold == npcGold - offer.Price && SamePlayer(game, playerGold, playerSkills) &&
                store.ReadNpcActor(traineeId, store.ResolveNpcArchetype(traineeId)).Skill(skill) == beforeSkill && trainee.Resident.Id == traineeId &&
                inventoryOwner.Binding.PersonId == traineeId && trainer.Data.hash == trainerHash && traineeBody.Entity.Level == nativeNpcLevel,
                "Paid running lesson changed player state, NPC identity, or skill before its finish time.");
            yield return null;
            Require(store.ReadNpcActor(traineeId, store.ResolveNpcArchetype(traineeId)).Skill(skill) == beforeSkill &&
                Experience(store.ReadNpcProgress(traineeId), skill) == beforeExperience,
                "Native training awarded its skill before three game hours elapsed.");
            Require(traineeBody.Entity.Skills.GetPermanentSkillValue((DFCareer.Skills)skill) == beforeNativeSkill,
                "Native training changed the live NPC skill before three game hours elapsed.");
            // The only world-time control in this probe: advance from the paid
            // running stage to its persisted deadline, never at service start.
            double remaining = running.FinishAt - Now(); Require(remaining > 0, "Training timer elapsed before controlled finish check.");
            double controlledAdvance = remaining + 1d;
            DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)controlledAdvance);
            deadline = Time.realtimeSinceStartup + 30; NpcActionJob completed = running;
            while (Time.realtimeSinceStartup < deadline)
            {
                completed = store.ReadNpcAction(accepted.Id);
                if (completed == null || NpcActionExecutor.IsTerminal(completed)) break;
                yield return null;
            }
            Require(completed != null && completed.Phase == NpcActionPhase.Completed && completed.ServiceStep == 1 &&
                store.ReadNpcInventory(traineeId).WalletGold == npcGold - offer.Price && SamePlayer(game, playerGold, playerSkills) &&
                store.ReadNpcActor(traineeId, store.ResolveNpcArchetype(traineeId)).Skill(skill) >= beforeSkill &&
                Experience(store.ReadNpcProgress(traineeId), skill) > beforeExperience &&
                traineeBody.Entity.Skills.GetPermanentSkillValue((DFCareer.Skills)skill) ==
                    store.ReadNpcActor(traineeId, store.ResolveNpcArchetype(traineeId)).Skill(skill) &&
                trainee.Resident.Id == traineeId && inventoryOwner.Binding.PersonId == traineeId && trainer.Data.hash == trainerHash &&
                traineeBody.Entity.Level == nativeNpcLevel,
                "Native temple lesson did not complete with one NPC-only receipt-bound result.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TEMPLE_TRAINING_NATIVE_PASS: resident=" + traineeId + "; trainer=" + offer.Trainer.Id +
                "; trainerHash=" + trainerHash + "; skill=" + skill + "; price=" + offer.Price + "; npcGold=" + npcGold + "->" +
                store.ReadNpcInventory(traineeId).WalletGold + "; skill=" + beforeSkill + "->" +
                store.ReadNpcActor(traineeId, store.ResolveNpcArchetype(traineeId)).Skill(skill) + "; rawExperience=" + beforeExperience + "->" +
                Experience(store.ReadNpcProgress(traineeId), skill) + "; controlledBudget=" + controlledBudget +
                "; serviceClock=" + NativeNpcTempleTraining.DurationSeconds + "; startNoTimeSkip=true; beforeFinishNoSkill=true; playerUnchanged=true; sameIdentity=true.");
        }
        private static double Now() { var time = DaggerfallUnity.Instance.WorldTime.Now; return time.ToSeconds() + (time.Second - Math.Floor(time.Second)); }
        private static bool InteriorAdmissionReady(StreetPopulation streets)
        {
            var geometry = typeof(StreetPopulation).GetField("indoorGeometry", Private).GetValue(streets) as InteriorGeometry;
            return geometry != null && geometry.IsComplete;
        }
        // Select only an already materialized ordinary resident. The owned
        // fixture budget below is explicit test data, not a production income
        // source, and remains restricted to this command-line probe.
        private static bool TryFindEligibleTempleEntry(StreetPopulation streets, WorldStateStore store, int templeKey,
            out ResidentActor selected, out NpcActionOption entry, out string diagnostic)
        {
            selected = null; entry = null;
            var candidates = new List<ResidentActor>();
            foreach (var actor in UnityEngine.Object.FindObjectsOfType<ResidentActor>())
                if (actor && actor.gameObject.activeInHierarchy && actor.Resident != null) candidates.Add(actor);
            candidates.Sort((left, right) => string.CompareOrdinal(left.Resident.Id, right.Resident.Id));
            int active = 0, ordinary = 0, levelOne = 0, missingBinding = 0, enterOffers = 0;
            var eligible = new List<ResidentActor>();
            foreach (var actor in candidates)
            {
                active++;
                if (!actor.Resident.IsAlive || actor.Resident.IsQuestProtected || actor.Resident.NativeIdentity != null ||
                    actor.Resident.AgeGroup != ResidentAgeGroup.Adult || actor.IsSleeping || actor.IsConversing || streets.IsAlarmed(actor.Resident.Id) ||
                    store.ActiveNpcTask(actor.Resident.Id) != null) continue;
                ordinary++;
                var body = actor.GetComponent<DaggerfallEntityBehaviour>();
                if (body == null || body.Entity == null || body.Entity.Level != 1) continue;
                levelOne++;
                var owner = actor.GetComponent<NativeNpcInventoryOwner>();
                if (owner == null || owner.Binding == null || owner.Binding.PersonId != actor.Resident.Id)
                { missingBinding++; continue; }
                eligible.Add(actor);
            }
            var game = GameManager.Instance;
            string name = game == null || game.TalkManager == null ? null : game.TalkManager.GetBuildingNameForBuildingKey(templeKey);
            if (!string.IsNullOrWhiteSpace(name)) foreach (var actor in eligible)
            {
                foreach (var option in streets.ConversationActionOptions(actor.Resident.Id, "Please enter " + name + "."))
                {
                    if (option == null || option.Kind != NpcActionKind.Enter || option.Target == null ||
                        option.Target.Kind != NpcActionTargetKind.Building || option.Target.BuildingKey != templeKey) continue;
                    enterOffers++; selected = actor; entry = option;
                    diagnostic = "selected=" + actor.Resident.Id + "; controlledBudget=" + Guild.nonMemberTrainingCost +
                        "; exactTempleEnter=true";
                    return true;
                }
            }
            diagnostic = "activeResidents=" + active + "; ordinaryAdults=" + ordinary + "; levelOne=" + levelOne +
                "; missingBinding=" + missingBinding + "; eligibleEnterOffers=" + enterOffers +
                "; templeName=" + (string.IsNullOrWhiteSpace(name) ? "absent" : name);
            return false;
        }

        private static bool TrySetControlledFixtureBudget(WorldStateStore store, ResidentActor trainee,
            out int budget, out string reason)
        {
            budget = 0; reason = null;
            try
            {
                if (store == null || !trainee || trainee.Resident == null) { reason = "resident is unavailable"; return false; }
                var body = trainee.GetComponent<DaggerfallEntityBehaviour>();
                var owner = trainee.GetComponent<NativeNpcInventoryOwner>();
                if (body == null || body.Entity == null || body.Entity.Level < 1 || owner == null || owner.Binding == null ||
                    owner.Binding.PersonId != trainee.Resident.Id) { reason = "existing native body or inventory binding is unavailable"; return false; }
                if (!NativeNpcInventoryBridge.TryCapture(store, owner.Binding, out reason)) return false;
                var current = store.ReadNpcInventory(trainee.Resident.Id);
                budget = checked(Guild.nonMemberTrainingCost * body.Entity.Level);
                if (current == null || current.Revision != owner.Binding.Revision || budget < 1) { reason = "saved wallet binding changed"; return false; }
                if (!store.CommitNpcInventoryTransaction(
                    "temple-training-controlled-budget:" + trainee.Resident.Id + ":" + current.Revision,
                    new[] { new NpcInventoryUpdate { PersonId = trainee.Resident.Id, ExpectedRevision = current.Revision,
                        WalletGold = budget, Items = NativeNpcInventoryBridge.CaptureItems(owner.Binding.Items) } }))
                { reason = "controlled wallet update was rejected"; return false; }
                if (!NativeNpcInventoryBridge.TryCapture(store, owner.Binding, out reason)) return false;
                current = store.ReadNpcInventory(trainee.Resident.Id);
                if (current == null || current.WalletGold != budget || current.Revision != owner.Binding.Revision)
                { reason = "controlled wallet update did not retain its bound revision"; return false; }
                return true;
            }
            catch (Exception error) { reason = error.Message; return false; }
        }

        private static bool TryFindCurrentOffer(StreetPopulation streets, DaggerfallInterior room, WorldStateStore store,
            Func<StaticNPC, NativePerson> resolve, string expectedTraineeId, out ResidentActor trainee, out NpcActionOption choice, out string diagnostic)
        {
            trainee = null; choice = null;
            var actors = new List<ResidentActor>(UnityEngine.Object.FindObjectsOfType<ResidentActor>());
            actors.Sort((left, right) => string.CompareOrdinal(left == null || left.Resident == null ? string.Empty : left.Resident.Id,
                right == null || right.Resident == null ? string.Empty : right.Resident.Id));
            var trainers = new List<StaticNPC>(room.GetComponentsInChildren<StaticNPC>(true));
            trainers.Sort((left, right) => (left == null ? 0 : left.Data.hash).CompareTo(right == null ? 0 : right.Data.hash));
            var reasons = new HashSet<string>(); int active = 0, direct = 0;
            foreach (var actor in actors)
            {
                if (!actor || !actor.gameObject.activeInHierarchy || actor.Resident == null || actor.Resident.Id != expectedTraineeId ||
                    actor.gameObject.scene != room.gameObject.scene) continue;
                active++;
                if (actor.IsSleeping || actor.IsConversing) { reasons.Add(actor.IsSleeping ? "resident sleeping" : "resident conversing"); continue; }
                int offeredLessons = 0;
                foreach (var option in streets.ConversationActionOptions(actor.Resident.Id, "Train me."))
                    if (option.Kind == NpcActionKind.Purchase && option.Target != null && option.Target.ContainerId == NativeNpcTempleTraining.TargetContainerId)
                    { offeredLessons++; choice = option; }
                if (choice != null)
                {
                    // Exercise a non-first native lesson whenever this trainer
                    // offers more than one eligible skill to the actual resident.
                    trainee = actor;
                    diagnostic = "resident=" + actor.Resident.Id + "; trainer=" + choice.Target.PersonId +
                        "; offeredLessons=" + offeredLessons + "; selectedSkill=" + choice.Target.ItemId + "; offer ready";
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TEMPLE_TRAINING_CATALOG: " + diagnostic);
                    return true;
                }
                foreach (var trainer in trainers)
                for (int skill = 0; skill < 35; skill++)
                {
                    NativeNpcTempleTrainingOffer offer; string reason;
                    if (NativeNpcTempleTraining.TryPrepareOffer(store, actor, trainer, resolve, room.BuildingData.FactionId, skill, false, out offer, out reason))
                    {
                        direct++;
                        int gold = store.ReadNpcInventory(actor.Resident.Id).WalletGold;
                        reasons.Add(gold < offer.Price ? "wallet " + gold + " < native price " + offer.Price : "offer admitted but approach/action option unavailable");
                        break;
                    }
                    if (!string.IsNullOrEmpty(reason)) reasons.Add(reason);
                }
            }
            var orderedReasons = new List<string>(reasons); orderedReasons.Sort(StringComparer.Ordinal);
            diagnostic = "activeResidents=" + active + "; staticNpcs=" + trainers.Count + "; directOffers=" + direct + "; reasons=" +
                string.Join(" | ", orderedReasons.GetRange(0, Math.Min(4, orderedReasons.Count)).ToArray());
            return false;
        }
        private static bool StagePlayerNearExistingActor(GameManager game, ResidentActor actor)
        {
            if (!game || !actor || !actor.gameObject.activeInHierarchy) return false;
            Vector3 away = actor.transform.right; away.y = 0;
            if (away.sqrMagnitude < .01f) away = Vector3.forward;
            Vector3 point = actor.transform.position - Vector3.up + away.normalized * 1.5f + Vector3.up;
            game.PlayerMotor.transform.position = point; Physics.SyncTransforms();
            return Vector3.Distance(game.PlayerMotor.transform.position, actor.transform.position) <= 2.1f &&
                CivilianResponses.Sees(game.PlayerMotor.transform.position, actor.transform.position, 3);
        }
        private static short[] PlayerSkills(GameManager game) { var values = new short[35]; for (int i = 0; i < values.Length; i++) values[i] = game.PlayerEntity.Skills.GetPermanentSkillValue(i); return values; }
        private static long Experience(NpcActorProgress progress, int skill) { if (progress == null) return 0; var row = progress.Skills.Find(item => item.Skill == skill); return row == null ? 0 : row.Experience; }
        private static bool SamePlayer(GameManager game, int gold, short[] skills) { if (game.PlayerEntity.GetGoldAmount() != gold) return false; for (int i = 0; i < skills.Length; i++) if (game.PlayerEntity.Skills.GetPermanentSkillValue(i) != skills[i]) return false; return true; }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
