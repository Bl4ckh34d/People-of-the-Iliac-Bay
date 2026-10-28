// Production night controller and Core state run unchanged. Doubles supply only
// native scene hierarchy, quest ownership, clock and talk-window state.
using System;
using System.Collections.Generic;
using System.Reflection;
using PeopleOfTheIliacBay.Core;

namespace UnityEngine
{
    public class Object { public static implicit operator bool(Object value) => value != null; }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform => gameObject.transform;
        public T GetComponent<T>() where T : Component => gameObject.GetComponent<T>();
    }
    public class MonoBehaviour : Component { }
    public sealed class Transform : Object
    {
        public GameObject gameObject;
        public Transform parent;
        public int Position;
        public bool IsChildOf(Transform owner)
        { for (Transform at = this; at != null; at = at.parent) if (at == owner) return true; return false; }
    }
    public sealed class GameObject : Object
    {
        readonly Dictionary<Type, Component> components = new Dictionary<Type, Component>();
        public readonly Transform transform;
        public bool activeSelf = true;
        public bool activeInHierarchy => activeSelf && (transform.parent == null || transform.parent.gameObject.activeInHierarchy);
        public string Inventory = "original item UID";
        public GameObject() { transform = new Transform { gameObject = this }; }
        public T GetComponent<T>() where T : Component => components.TryGetValue(typeof(T), out var c) ? (T)c : null;
        public T AddComponent<T>() where T : Component, new()
        { var component = new T { gameObject = this }; components.Add(typeof(T), component); return component; }
        public void SetActive(bool value) { activeSelf = value; }
    }
    public static class Time { public static float unscaledTime; }
}
namespace DaggerfallWorkshop
{
    public sealed class DaggerfallAction : UnityEngine.MonoBehaviour { }
    public sealed class DaggerfallUnity
    {
        public static DaggerfallUnity Instance = new DaggerfallUnity();
        public WorldClock WorldTime = new WorldClock();
    }
    public sealed class WorldClock { public WorldClock Now => this; public double Seconds; public double ToSeconds() => Seconds; }
}
namespace DaggerfallWorkshop.Utility
{ public static class RDBLayout { public static bool IsNPCFlat(int archive) => archive == 182; } }
namespace DaggerfallWorkshop.Game.Questing
{
    public class Person { public bool IsHidden, IsDestroyed; }
    public sealed class QuestResourceBehaviour : UnityEngine.MonoBehaviour { public object TargetResource; }
    public sealed class QuestMachine
    {
        public static QuestMachine Instance = new QuestMachine();
        public readonly HashSet<int> Listeners = new HashSet<int>();
        public readonly Dictionary<int, Person> Questors = new Dictionary<int, Person>();
        public bool HasFactionListener(int id) => Listeners.Contains(id);
        public Person ActiveQuestor(DaggerfallWorkshop.Game.StaticNPC.NPCData data) => Questors.TryGetValue(data.hash, out var p) ? p : null;
    }
}
namespace DaggerfallWorkshop.Game
{
    public sealed class StaticNPC : UnityEngine.MonoBehaviour
    {
        public enum Context { Custom, Dungeon, Building }
        public struct NPCData { public Context context; public int hash, factionID, billboardArchiveIndex, billboardRecordIndex; }
        public NPCData Data;
    }
    public sealed class BuildingDirectory : UnityEngine.MonoBehaviour
    {
        public int MapID;
        public readonly List<StaticNPC> People = new List<StaticNPC>();
        public T[] GetComponentsInChildren<T>(bool includeInactive) where T : UnityEngine.Component
        { var found = new List<T>(); foreach (var npc in People) if (npc.transform.IsChildOf(transform) && (includeInactive || npc.gameObject.activeInHierarchy)) found.Add((T)(object)npc); return found.ToArray(); }
    }
    public sealed class GameManager
    {
        public static GameManager Instance = new GameManager();
        public bool IsReady = true;
        public bool Playing = true;
        public bool IsPlayingGame() => Playing;
        public PlayerEnterExit PlayerEnterExit = new PlayerEnterExit();
        public PlayerGPS PlayerGPS = new PlayerGPS();
        public StreamingWorld StreamingWorld = new StreamingWorld();
        public PlayerEntity PlayerEntity = new PlayerEntity();
        public TalkManager TalkManager = new TalkManager();
    }
    public sealed class PlayerEnterExit { public bool IsPlayerInside; }
    public sealed class PlayerGPS { public bool HasCurrentLocation = true; public int CurrentMapID; }
    public sealed class StreamingWorld { public BuildingDirectory Directory; public BuildingDirectory GetCurrentBuildingDirectory() => Directory; }
    public sealed class PlayerEntity { public Globals GlobalVars = new Globals(); }
    public sealed class Globals { public bool Lifted; public bool GetGlobalVar(int index) => Lifted; }
    public sealed class TalkManager { public enum NPCType { Static, Mobile } public NPCType CurrentNPCType; public StaticNPC StaticNPC; }
    public sealed class DaggerfallUI
    {
        public static DaggerfallUI Instance = new DaggerfallUI();
        public static WindowManager UIManager = new WindowManager();
        public object TalkWindow = new object();
    }
    public sealed class WindowManager { public bool Open; public bool ContainsWindow(object window) => Open; }
}
namespace PeopleOfTheIliacBay.DFU
{
    public sealed class ResidentActor : UnityEngine.MonoBehaviour { }
    public sealed class GuardActor : UnityEngine.MonoBehaviour { }
    internal static class NativeExteriorNpcIdentity
    {
        internal static int Hash(DaggerfallWorkshop.Game.StaticNPC npc, UnityEngine.Transform directory) => npc.Data.hash;
        internal static string PersonId(int settlement, int hash) => "npc:" + settlement + ":-1:" + hash;
    }
    public sealed partial class PeopleOfTheIliacBayMod
    {
        private WorldStateStore store;
        private RuntimeOptions options = new RuntimeOptions(true, 6, 22, 1, false);
        private SettlementPopulation current;
        private bool saveFault;
        internal static bool IsAuthoredNativeIndividual(int faction) => faction == 999;
        public PeopleOfTheIliacBayMod(WorldStateStore world, SettlementPopulation town) { store = world; current = town; }
        private void Invoke(string method)
        {
            var target = GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic);
            if (target == null) throw new InvalidOperationException("Missing production night controller: " + method);
            target.Invoke(this, null);
        }
        public void Tick() { UnityEngine.Time.unscaledTime += 2; Invoke("TickExteriorStaticNpcNight"); }
        public void Restore() { Invoke("RestoreExteriorStaticNpcNight"); }
        public void Configure(bool enabled, int wake = 6, int sleep = 22) { options = new RuntimeOptions(enabled, wake, sleep, 1, false); }
        public void SetFault(bool value) { saveFault = value; }
        public void SetTown(SettlementPopulation value) { current = value; }
        public string Context(DaggerfallWorkshop.Game.StaticNPC npc, double seconds)
        {
            var target = GetType().GetMethod("ExteriorStaticNpcNightContext", BindingFlags.Instance | BindingFlags.NonPublic);
            if (target == null) throw new InvalidOperationException("Missing production night context");
            return (string)target.Invoke(this, new object[] { npc, seconds });
        }
    }
}
