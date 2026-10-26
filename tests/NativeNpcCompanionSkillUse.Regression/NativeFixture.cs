// CPU boundary fixture. Native formula and Unity scene operations are controlled;
// the production swing adapter and complete Core store remain real.
using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace UnityEngine
{
    public class Object
    {
        public static implicit operator bool(Object value) {return value!=null;}
        public static T[] FindObjectsOfType<T>() {return new T[0];}
    }
    public class GameObject : Object
    {
        public bool activeInHierarchy=true;
        public readonly Transform transform=new Transform();
        private readonly Dictionary<Type,Component> components=new Dictionary<Type,Component>();
        public T Add<T>() where T:Component,new() {var value=new T {gameObject=this};components[typeof(T)]=value;return value;}
        public T GetComponent<T>() where T:class {return components.TryGetValue(typeof(T),out var value)?value as T:null;}
    }
    public class Component : Object
    {
        public GameObject gameObject;
        public Transform transform {get{return gameObject.transform;}}
        public T GetComponent<T>() where T:class {return gameObject.GetComponent<T>();}
    }
    public class Transform {public Vector3 position;}
    public struct Vector3 { }
}
namespace DaggerfallConnect
{
    public class DFCareer {public enum Skills {Archery=32}}
}
namespace DaggerfallWorkshop
{
    public enum MobileTypes {Knight_CityWatch=146}
}
namespace DaggerfallWorkshop.Game.Items
{
    public enum ItemGroups {Weapons}
    public enum EquipSlots {RightHand}
    public class RepairData {public bool IsBeingRepaired() {return false;}}
    public class DaggerfallUnityItem
    {
        public ItemGroups ItemGroup;
        public int currentCondition=100;
        public readonly RepairData RepairData=new RepairData();
        public bool IsEnchanted;
        public ulong UID=1;
        public short Skill=29;
        public short GetWeaponSkillIDAsShort() {return Skill;}
    }
    public class ItemCollection
    {
        public DaggerfallUnityItem Weapon;
        public DaggerfallUnityItem GetItem(ulong uid) {return Weapon!=null && Weapon.UID==uid?Weapon:null;}
    }
    public class ItemEquipTable
    {
        public DaggerfallUnityItem Weapon;
        public DaggerfallUnityItem GetItem(EquipSlots slot) {return Weapon;}
    }
}
namespace DaggerfallWorkshop.Game.Entity
{
    using DaggerfallWorkshop.Game.Items;
    public enum MobileTeams {None,PlayerAlly}
    public class SkillValues
    {
        private readonly short[] skills=new short[35];
        public int GetLiveSkillValue(int skill) {return skills[skill];}
        public short GetPermanentSkillValue(int skill) {return skills[skill];}
        public void SetPermanentSkillValue(int skill,short value) {skills[skill]=value;}
    }
    public class DaggerfallEntity
    {
        public int CurrentHealth=100;
        public bool IsParalyzed,IsInvisible;
        public MobileTeams Team;
        public readonly ItemCollection Items=new ItemCollection();
        public readonly ItemEquipTable ItemEquipTable=new ItemEquipTable();
        public readonly SkillValues Skills=new SkillValues();
    }
    public class EnemyEntity:DaggerfallEntity {public MobileEnemy MobileEnemy=new MobileEnemy();}
    public class MobileEnemy {public int ID;}
    public class DaggerfallEntityBehaviour:UnityEngine.Component
    {
        public DaggerfallEntity Entity=new DaggerfallEntity();
        public bool IgnoreDamage;
        public Action OnDamage;
        public void DamageHealthFromSource(DaggerfallEntityBehaviour source,int damage,bool blood,UnityEngine.Vector3 position)
        {if(!IgnoreDamage)Entity.CurrentHealth=Math.Max(0,Entity.CurrentHealth-damage);OnDamage?.Invoke();}
    }
}
namespace DaggerfallWorkshop.Game
{
    using DaggerfallWorkshop.Game.Entity;
    public class GameManager {public static readonly GameManager Instance=new GameManager();public DaggerfallEntityBehaviour PlayerEntityBehaviour;}
    public class DaggerfallEnemy:UnityEngine.Component {public ulong LoadID=1;public bool QuestSpawn;}
    public class StaticNPC:UnityEngine.Component { }
    public class EnemyMotor:UnityEngine.Component
    {public bool IsHostile;public void MakeEnemyHostileToAttacker(DaggerfallEntityBehaviour body) {IsHostile=true;}}
    public class EnemySenses:UnityEngine.Component {public DaggerfallEntityBehaviour Target;}
    public class EnemySounds:UnityEngine.Component
    {public void PlayHitSound(Items.DaggerfallUnityItem item) {}public void PlayMissSound(Items.DaggerfallUnityItem item) {}}
}
namespace DaggerfallWorkshop.Game.Formulas
{
    public static class FormulaHelper
    {
        public static int Damage=3;
        public static int CalculateAttackDamage(Entity.DaggerfallEntity source,Entity.DaggerfallEntity target,bool back,int modifier,Items.DaggerfallUnityItem weapon)
        {return Damage;}
    }
}
namespace DaggerfallWorkshop.Game.MagicAndEffects
{
    public enum EnchantmentPayloadFlags {Strikes}
    public class EntityEffectManager:UnityEngine.Component
    {public int DoItemEnchantmentPayloads(EnchantmentPayloadFlags flags,Items.DaggerfallUnityItem weapon,Items.ItemCollection items,Entity.DaggerfallEntityBehaviour target,int damage) {return damage;}}
}
namespace DaggerfallWorkshop.Game.Questing {public class QuestResourceBehaviour:UnityEngine.Component {}}
namespace PeopleOfTheIliacBay.DFU
{
    public class GuardActor:UnityEngine.Component { }
    public class ResidentActor:UnityEngine.Component
    {public Resident Resident;public bool IsSleeping,IsSeated;public void CaptureInventory() {}}
    public class DefenseScene
    {
        private static readonly object root=new object();
        public object Root=root;public bool PlayerPresent=true;
        public static DefenseScene For(UnityEngine.Component actor) {return new DefenseScene();}
    }
    public static class CivilianResponses {public static bool Sees(UnityEngine.Vector3 from,UnityEngine.Vector3 to,float range) {return true;}}
}
