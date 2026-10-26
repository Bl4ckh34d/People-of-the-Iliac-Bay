// Controlled scene boundaries for the linked production catalog/prepare/execute code.
// Native pricing, guild eligibility, payment and skill awards remain separate runtime checks.
using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace UnityEngine
{
    public class Object { public static implicit operator bool(Object value) { return value != null; } }
    public sealed class GameObject { public bool activeInHierarchy = true; public int scene = 1; }
    public sealed class Transform
    {
        public Vector3 position;
        public Vector3 forward = Vector3.forward;
        public Vector3 TransformPoint(Vector3 point) { return point; }
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3();
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 forward => new Vector3(0, 0, 1);
        public float sqrMagnitude => x * x + y * y + z * z;
        public Vector3 normalized => sqrMagnitude == 0 ? zero : this * (1f / (float)Math.Sqrt(sqrMagnitude));
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => a + -b;
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float b) => new Vector3(a.x * b, a.y * b, a.z * b);
        public static float Distance(Vector3 a, Vector3 b) => (float)Math.Sqrt((a - b).sqrMagnitude);
    }
}
namespace DaggerfallConnect
{
    public static class DFCareer { public enum Skills { } }
    public static class DFLocation { public enum BuildingTypes { Temple } }
}
namespace DaggerfallWorkshop
{
    public sealed class DaggerfallUnity
    {
        public static readonly DaggerfallUnity Instance = new DaggerfallUnity();
        public readonly TextProvider TextProvider = new TextProvider();
    }
    public sealed class TextProvider
    { public string GetSkillName(DaggerfallConnect.DFCareer.Skills skill) => "skill " + (int)skill; }
    public sealed class BuildingData
    { public DaggerfallConnect.DFLocation.BuildingTypes BuildingType; public int FactionId = 1; }
    public sealed class DaggerfallInterior : UnityEngine.Object
    {
        public readonly GameObject gameObject = new GameObject();
        public readonly Transform transform = new Transform();
        public readonly BuildingData BuildingData = new BuildingData();
        public DaggerfallWorkshop.Game.StaticNPC Trainer;
        public T[] GetComponentsInChildren<T>() { return new[] { (T)(object)Trainer }; }
    }
}
namespace DaggerfallWorkshop.Game
{
    public sealed class NpcData { public int hash = 10; }
    public sealed class StaticNPC : UnityEngine.Object
    {
        public readonly NpcData Data = new NpcData();
        public readonly GameObject gameObject = new GameObject();
        public readonly Transform transform = new Transform();
        public DaggerfallWorkshop.DaggerfallInterior Interior;
        public T GetComponentInParent<T>() { return (T)(object)Interior; }
    }
    public static class PlayerActivate
    { public static bool Open = true; public static bool IsBuildingOpen(DaggerfallConnect.DFLocation.BuildingTypes type) => Open; }
}
namespace PeopleOfTheIliacBay.DFU
{
    public sealed class ResidentActor : UnityEngine.Object
    { public Resident Resident = new Resident { Id = "trainee" }; public readonly Transform transform = new Transform { position = Vector3.up }; }
    public static class CivilianResponses { public static bool Sees(Vector3 from, Vector3 to, float distance) => true; }
    public sealed class NativeNpcTempleTrainingOffer
    { public NativePerson Trainer; public int Skill, Price; public long ExpectedInventoryRevision; }
    public static class NativeNpcTempleTraining
    {
        public const string ServiceCode = NpcShopCommerce.TrainingServiceCode;
        public const string TargetContainerId = NpcShopCommerce.TrainingTargetContainerId;
        public const double DurationSeconds = 10800;
        public static readonly HashSet<int> Skills = new HashSet<int>();
        public static bool Funded = true;
        public static int ExecutedSkill = -1;
        public static bool TryPrepareOffer(WorldStateStore store, ResidentActor actor, DaggerfallWorkshop.Game.StaticNPC trainer,
            Func<DaggerfallWorkshop.Game.StaticNPC, NativePerson> resolve, int faction, int skill, bool requireFunds,
            out NativeNpcTempleTrainingOffer offer, out string reason)
        {
            reason = "Unavailable native lesson.";
            offer = null;
            if (!Skills.Contains(skill) || requireFunds && !Funded) return false;
            offer = new NativeNpcTempleTrainingOffer { Trainer = resolve(trainer), Skill = skill, Price = 400, ExpectedInventoryRevision = 7 };
            reason = null;
            return true;
        }
        public static bool TryExecuteStage(WorldStateStore store, NpcActionJob job, ResidentActor actor,
            DaggerfallWorkshop.Game.StaticNPC trainer, Func<DaggerfallWorkshop.Game.StaticNPC, NativePerson> resolve,
            int faction, double now, out bool completed, out string reason)
        { ExecutedSkill = int.Parse(job.Target.ItemId); completed = false; reason = null; return true; }
    }
    public sealed partial class StreetPopulation
    {
        private readonly DaggerfallWorkshop.DaggerfallInterior interior = new DaggerfallWorkshop.DaggerfallInterior();
        private readonly object indoorGeometry = new object();
        private readonly WorldStateStore worldStore = new WorldStateStore(42);
        private readonly ResidentActor actor = new ResidentActor();
        public bool Reachable = true;
        public StreetPopulation()
        {
            interior.Trainer = new DaggerfallWorkshop.Game.StaticNPC { Interior = interior };
            NativeNpcTempleTraining.Skills.Clear();
            NativeNpcTempleTraining.Skills.Add(3); NativeNpcTempleTraining.Skills.Add(23);
            NativeNpcTempleTraining.Funded = true; NativeNpcTempleTraining.ExecutedSkill = -1;
            DaggerfallWorkshop.Game.PlayerActivate.Open = true;
        }
        public List<NpcActionOption> Offers(int existing = 0, bool semantic = true)
        {
            var options = new List<NpcActionOption>();
            for (int index = 0; index < existing; index++) options.Add(new NpcActionOption { Id = index + 1 });
            OfferNativeTempleTrainingAction(actor, null, options, semantic);
            return options;
        }
        public NpcActionTarget Target(int skill) => new NpcActionTarget { Kind = NpcActionTargetKind.IndoorPoint,
            SettlementId = 1, BuildingKey = 10, PersonId = "trainer", ContainerId = NativeNpcTempleTraining.TargetContainerId,
            ItemId = skill.ToString(), Quantity = 1, QuotedGold = 400 };
        public NpcActionJob Job(NpcActionOption option) => new NpcActionJob { ResidentId = actor.Resident.Id,
            Kind = option.Kind, Target = NpcActionExecutor.CopyTarget(option.Target) };
        public bool Prepare(NpcActionJob job, out string reason) => PrepareNativeTempleTrainingAction(actor, job, out reason);
        public bool Execute(NpcActionJob job, out string reason)
        { bool completed; return ExecuteNativeTempleTrainingAction(job, actor, 100, out completed, out reason); }
        private NpcActionTarget ActionPosition(Vector3 point) => new NpcActionTarget { Kind = NpcActionTargetKind.IndoorPoint, SettlementId = 1, BuildingKey = 10 };
        private Vector3 ActionWorldPoint(NpcActionTarget target) => Vector3.zero;
        private NativePerson KeeperPerson(DaggerfallWorkshop.Game.StaticNPC npc) => new NativePerson { Id = "trainer" };
        private bool QuestOwned(Resident resident) => false;
        private bool IsAlarmed(string residentId) => false;
        private double ActionNow() => 100;
        private List<GuardPoint3> IndoorPersonApproachRoute(Vector3 actual, Vector3 target, Vector3 away, float range)
            => Reachable ? new List<GuardPoint3> { new GuardPoint3() } : null;
        private static void AddActionOption(List<NpcActionOption> options, NpcActionKind kind, NpcActionTarget target, string label, string text, bool semantic)
        {
            if (options.Count >= (semantic ? NpcActionCandidateCatalog.MaximumCandidates : 24)) return;
            options.Add(new NpcActionOption { Id = options.Count + 1, Kind = kind, Target = target, Label = label });
        }
    }
}
