// The production binding and WorldStateStore run unchanged. These doubles supply
// only the engine component host, native spellbook storage and serialization boundary.
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace UnityEngine
{
    public class Object { public static implicit operator bool(Object value) => value != null; }
    public class MonoBehaviour : Object
    {
        public GameObject gameObject;
        public T GetComponent<T>() where T : MonoBehaviour => gameObject.GetComponent<T>();
    }
    public sealed class GameObject : Object
    {
        private readonly Dictionary<Type, MonoBehaviour> components = new Dictionary<Type, MonoBehaviour>();
        public T GetComponent<T>() where T : MonoBehaviour => components.TryGetValue(typeof(T), out var value) ? (T)value : null;
        public T AddComponent<T>() where T : MonoBehaviour, new()
        { var value = new T { gameObject = this }; components.Add(typeof(T), value); return value; }
    }
}
namespace DaggerfallWorkshop.Game.Entity
{
    public sealed class DaggerfallEntityBehaviour : UnityEngine.MonoBehaviour
    { public DaggerfallEntity Entity = new DaggerfallEntity(); }
    public sealed class DaggerfallEntity
    {
        private DaggerfallWorkshop.Game.MagicAndEffects.EffectBundleSettings[] book = Array.Empty<DaggerfallWorkshop.Game.MagicAndEffects.EffectBundleSettings>();
        public int SpellbookCount() => book.Length;
        public DaggerfallWorkshop.Game.MagicAndEffects.EffectBundleSettings[] SerializeSpellbook() => (DaggerfallWorkshop.Game.MagicAndEffects.EffectBundleSettings[])book.Clone();
        public void DeserializeSpellbook(DaggerfallWorkshop.Game.MagicAndEffects.EffectBundleSettings[] value)
        { book = value == null ? Array.Empty<DaggerfallWorkshop.Game.MagicAndEffects.EffectBundleSettings>() : (DaggerfallWorkshop.Game.MagicAndEffects.EffectBundleSettings[])value.Clone(); }
    }
}
namespace DaggerfallWorkshop.Game.MagicAndEffects
{
    public struct EffectBundleSettings { public string Name; public int StandardSpellIndex; }
    public sealed class EntityEffectManager : UnityEngine.MonoBehaviour
    {
        public DaggerfallWorkshop.Game.Entity.DaggerfallEntityBehaviour EntityBehaviour;
        public bool IsPlayerEntity;
        // Native EntityEffectManager uses this default on every fresh body.
        public bool UsePlayerCharacterSkillsForEnemyMagicCost = true;
    }
}
namespace DaggerfallWorkshop.Game.Serialization
{
    public static class SaveLoadManager
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions { IncludeFields = true };
        public static string Serialize(Type type, object value, bool pretty) => JsonSerializer.Serialize(value, type, Options);
        public static object Deserialize(Type type, string value) => JsonSerializer.Deserialize(value, type, Options);
    }
}
namespace PeopleOfTheIliacBay.DFU
{
    public sealed class ResidentActor : UnityEngine.MonoBehaviour { public PeopleOfTheIliacBay.Core.Resident Resident; }
    public sealed class NativeNpcVitalsBinding : UnityEngine.MonoBehaviour
    {
        public Action Rebaseline;
        public bool RebaselineAfterExternalCommit() { Rebaseline?.Invoke(); return true; }
    }
}
