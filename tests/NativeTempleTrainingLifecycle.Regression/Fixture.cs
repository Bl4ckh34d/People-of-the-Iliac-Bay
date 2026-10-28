using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
using DaggerfallConnect;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using UnityEngine;

// Only scene eligibility and native tally randomness are controlled. The exact
// production stage, payment, action receipts, skill ledger and restore execute.
namespace UnityEngine
{
    public class Object { public static implicit operator bool(Object value) { return value != null; } }
    public static class Random { public static int Range(int min, int max) { return 15; } }
    public static class Debug { public static void Log(string value) { } }
}
namespace DaggerfallConnect { public static class DFCareer { public enum Skills { } } }
namespace DaggerfallWorkshop.Game
{
    public static class GameManager { public static bool IsGamePaused; }
    public sealed class StaticNPC { }
    public sealed class DaggerfallSkills
    {
        private readonly short[] values = new short[35];
        public static int GetAdvancementMultiplier(DFCareer.Skills skill) { return 2; }
        public void SetPermanentSkillValue(DFCareer.Skills skill, short value) { values[(int)skill] = value; }
        public short Value(int skill) { return values[skill]; }
    }
}
namespace DaggerfallWorkshop.Game.Entity
{
    public sealed class DaggerfallEntityBehaviour
    { public readonly FakeEntity Entity = new FakeEntity(); }
    public sealed class FakeEntity { public readonly DaggerfallSkills Skills = new DaggerfallSkills(); }
}
namespace PeopleOfTheIliacBay.DFU
{
    public sealed class ResidentActor : UnityEngine.Object
    {
        public Resident Resident;
        public bool IsConversing;
        public readonly DaggerfallEntityBehaviour Body = new DaggerfallEntityBehaviour();
        public T GetComponent<T>() where T : class { return Body as T; }
    }
    public sealed class NativeNpcTempleTrainingOffer
    { public NativePerson Trainer; public int Skill, Price; public long ExpectedInventoryRevision, ExpectedActorRevision; }
    public static class NativeNpcTempleTraining
    {
        public const string ServiceCode = NpcShopCommerce.TrainingServiceCode;
        public const string TargetContainerId = NpcShopCommerce.TrainingTargetContainerId;
        // This replaces live trainer discovery only; it reads real actor/wallet
        // revisions so unrelated actors can legitimately have identical values.
        private static bool TryPrepareOffer(WorldStateStore store, ResidentActor trainee, StaticNPC trainerNpc,
            Func<StaticNPC, NativePerson> resolveTrainer, int faction, int skill, bool funds,
            out NativeNpcTempleTrainingOffer offer, out string reason)
        {
            offer = null; reason = "Unavailable trainer.";
            if (trainee.Resident == null) return false;
            var inventory = store.ReadNpcInventory(trainee.Resident.Id);
            if (funds && inventory.WalletGold < 100) return false;
            offer = new NativeNpcTempleTrainingOffer { Trainer = resolveTrainer(trainerNpc), Skill = skill, Price = 100,
                ExpectedInventoryRevision = inventory.Revision,
                ExpectedActorRevision = store.ReadNpcActor(trainee.Resident.Id, store.ResolveNpcArchetype(trainee.Resident.Id)).Revision };
            reason = null; return true;
        }
        // PRODUCTION_METHODS
        private static void Require(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        private static NpcActionJob Start(WorldStateStore store, ResidentActor actor)
        {
            var job = new NpcActionJob { Id = "lesson", ProposalId = "lesson", ResidentId = actor.Resident.Id,
                Kind = NpcActionKind.Purchase, Phase = NpcActionPhase.Accepted, Revision = 1, AcceptedAt = 100, UpdatedAt = 100,
                DeadlineAt = 11500, ServiceCode = ServiceCode, ServicePrice = 100,
                ExpectedInventoryRevision = store.ReadNpcInventory(actor.Resident.Id).Revision,
                Target = new NpcActionTarget { Kind = NpcActionTargetKind.IndoorPoint, SettlementId = 7, BuildingKey = 10,
                    PersonId = "trainer", ContainerId = TargetContainerId, ItemId = "23", Quantity = 1, QuotedGold = 100 } };
            string reason; Require(Program.Accept(store, job, out reason), reason);
            Require(store.ObserveNpcAction(job.Id, 1, 100, NpcActionObservation.Arrival()), "arrival");
            return store.ReadNpcAction(job.Id);
        }
        private static bool Execute(WorldStateStore store, NpcActionJob job, ResidentActor actor, double now, out bool completed)
        { string reason; return TryExecuteStage(store, job, actor, new StaticNPC(), npc => new NativePerson { Id = "trainer" }, 1, now, out completed, out reason); }
        private static WorldStateStore Fresh(out ResidentActor actor, out ResidentActor other)
        {
            GameManager.IsGamePaused = false;
            var store = new WorldStateStore(723u);
            var people = store.GetOrCreate(7, new[] { 10, 11 }, 1);
            actor = new ResidentActor { Resident = people.Households[0].Residents[0] };
            other = new ResidentActor { Resident = people.Households[1].Residents[0] };
            Require(store.InitializeNpcInventory(actor.Resident.Id, 1000, new List<NpcInventoryItem>()), "owner inventory");
            Require(store.InitializeNpcInventory(other.Resident.Id, 1000, new List<NpcInventoryItem>()), "other inventory");
            return store;
        }
        public static int Run()
        {
            int failed = 0;
            foreach (var test in new (string Name, Action Body)[] {
                ("another resident cannot pay or receive the selected lesson", () => {
                    ResidentActor actor, other; var store = Fresh(out actor, out other); var job = Start(store, actor); bool completed;
                    Require(!Execute(store, job, other, 100, out completed), "Wrong native resident started owner's lesson.");
                    Require(store.ReadNpcInventory(actor.Resident.Id).WalletGold == 1000 && store.ReadNpcAction(job.Id).ServiceStep == 0,
                        "Rejected native body changed payment or stage.");
                }),
                ("exact selected lesson pays once then restores and awards only that skill", () => {
                    ResidentActor actor, other; var store = Fresh(out actor, out other); var job = Start(store, actor); bool completed;
                    Require(Execute(store, job, actor, 100, out completed) && !completed, "start");
                    Require(!Execute(store, job, actor, 100, out completed), "stale start replay");
                    var restored = new WorldStateStore(1); restored.Restore(store.Snapshot()); store = restored;
                    job = store.ReadNpcAction(job.Id); var before = store.ReadNpcActor(actor.Resident.Id, store.ResolveNpcArchetype(actor.Resident.Id));
                    Require(job.Target.ItemId == "23" && job.ServiceStep == 1 && job.FinishAt == 10900, "saved exact lesson/time");
                    Require(Execute(store, job, actor, 10899, out completed) && !completed && store.ReadNpcProgress(actor.Resident.Id) == null, "early skill award");
                    Require(Execute(store, job, actor, 10900, out completed) && completed, "finish");
                    Require(!Execute(store, job, actor, 10900, out completed), "completed replay");
                    var progress = store.ReadNpcProgress(actor.Resident.Id);
                    Require(progress.Skills.Count == 1 && progress.Skills[0].Skill == 23 && progress.Skills[0].Experience == 30, "wrong skill or tally");
                    Require(store.ReadNpcInventory(actor.Resident.Id).WalletGold == 900 && store.ReadNpcInventory(other.Resident.Id).WalletGold == 1000,
                        "wrong wallet or second payment");
                    var after = store.ReadNpcActor(actor.Resident.Id, store.ResolveNpcArchetype(actor.Resident.Id));
                    Require(actor.Body.Entity.Skills.Value(23) == after.Skill(23), "native trained skill mismatch");
                    var final = new WorldStateStore(2); final.Restore(store.Snapshot());
                    Require(final.ReadNpcAction(job.Id).Phase == NpcActionPhase.Completed && final.ReadNpcProgress(actor.Resident.Id).Skills[0].Experience == 30 &&
                        final.ReadNpcInventory(actor.Resident.Id).WalletGold == 900, "completed result restore");
                }),
                ("paused or conversing native lesson cannot start", () => {
                    ResidentActor actor, other; var store = Fresh(out actor, out other); var job = Start(store, actor); bool completed;
                    GameManager.IsGamePaused = true; Require(!Execute(store, job, actor, 100, out completed), "paused payment");
                    GameManager.IsGamePaused = false; actor.IsConversing = true; Require(!Execute(store, job, actor, 100, out completed), "conversation payment");
                    Require(store.ReadNpcInventory(actor.Resident.Id).WalletGold == 1000, "rejected lesson changed wallet");
                }) })
            {
                try { test.Body(); Console.WriteLine("PASS " + test.Name); }
                catch (Exception error) { failed++; Console.WriteLine("FAIL " + test.Name + ": " + error.Message); }
            }
            Console.WriteLine("Native temple training lifecycle: " + (3 - failed) + "/3 passed.");
            return failed == 0 ? 0 : 1;
        }
    }
}
