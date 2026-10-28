using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;
using PeopleOfTheIliacBay.DFU;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.MagicAndEffects;
using UnityEngine;
internal static class Program
{
    private static int Main()
    {
        try {StreetPopulation.VerifyStandingRest();Console.WriteLine("PASS standing booked-bed recovery and interrupted/unarrived rejection");return 0;}
        catch(Exception error) {Console.Error.WriteLine(error);return 1;}
    }
}
namespace UnityEngine
{
    public class Object {public static implicit operator bool(Object value) {return value!=null;}}
    public sealed class Transform {public Transform parent;}
    public sealed class GameObject {public bool activeInHierarchy=true;}
    public static class Time {public static float unscaledTime;}
    public static class Debug {public static void Log(string value) {}}
}
namespace DaggerfallWorkshop.Game
{
    public sealed class GameManager
    {
        public static GameManager Instance=new GameManager();
        public sealed class EnterExit {public bool IsPlayerInsideDungeon;}
        public EnterExit PlayerEnterExit=new EnterExit();
    }
}
namespace DaggerfallWorkshop.Game.Entity
{
    public sealed class DaggerfallEntityBehaviour : UnityEngine.Object {public Entity Entity=new Entity();}
    public sealed class Entity
    {
        public int CurrentHealth=10,MaxHealth=100,CurrentMagicka=0,MaxMagicka=80;
        public sealed class CareerData {public bool NoRegenSpellPoints;}
        public CareerData Career=new CareerData();public bool Quiesce;
        public void SetHealth(int value) {CurrentHealth=value;}
        public void SetMagicka(int value) {CurrentMagicka=value;}
    }
}
namespace DaggerfallWorkshop.Game.MagicAndEffects
{public sealed class EntityEffectManager : UnityEngine.Object {public int PoisonCount,DiseaseCount;}}
namespace PeopleOfTheIliacBay.DFU
{
    public sealed class NativeNpcVitalsBinding : UnityEngine.Object
    {
        public Entity Body;public void Capture() {}
        public bool TryReadCurrent(out int hp,out int maxHp,out int mp,out int maxMp)
        {hp=Body.CurrentHealth;maxHp=Body.MaxHealth;mp=Body.CurrentMagicka;maxMp=Body.MaxMagicka;return true;}
    }
    public static class NativeNpcRestTreatment {public static int RecoveryRatePerGameHour(Entity entity) {return 10;}}
    public sealed class ResidentActor : UnityEngine.Object
    {
        public GameObject gameObject=new GameObject();public Transform transform=new Transform();
        public bool IsSleeping,IsConversing;
        public readonly Dictionary<Type,object> Components=new Dictionary<Type,object>();
        public T GetComponent<T>() where T:class {return Components[typeof(T)] as T;}
    }
    public sealed partial class StreetPopulation
    {
        private sealed class HomeSleepClock
        {internal ResidentActor Actor;internal double CheckedAt,Seconds;internal int Health,Magicka;internal bool AtHomeAsleep,Eligible;internal float NextCheck;}
        private sealed class Interior : UnityEngine.Object {internal Transform transform=new Transform();}
        private sealed class Bed {internal int RecordIndex=0;}
        private sealed class Follower {internal bool Arrived=true,Leaving=false;}
        private sealed class TravelerPlan
        {internal TravelerPerson Person;internal Resident Resident;internal Interior RoomInterior;internal Bed Bed;internal Follower RoomFollower;
         internal TravelerVisit Visit;internal TravelerJourney Journey;}
        private WorldStateStore worldStore;private object homeSleepEpoch;
        private readonly Dictionary<string,HomeSleepClock> homeSleepClocks=new Dictionary<string,HomeSleepClock>();
        private Interior interior;
        private bool IsAlarmed(string id) {return false;}
        private bool QuestOwned(Resident person) {return false;}
        public static void VerifyStandingRest()
        {
            foreach(int blocked in new[]{0,1,2})
            {
                var test=new StreetPopulation {worldStore=new WorldStateStore(1),interior=new Interior()};
                test.worldStore.GetOrCreate(1,new int[0]);test.worldStore.RegisterLodgingBeds(1,7,new[]{new LodgingBed {Id="bed:0"}});
                var person=new TravelerPerson {Id="traveler:test",Name="Test Guest",HomeSettlement=2,Race=1};
                var visit=new TravelerVisit {Id="visit",PersonId=person.Id,SettlementId=1,TavernKey=7,Nights=2,DesiredArrival=100,CommuteSeconds=10,
                    Route=new List<SavedRoutePoint>{new SavedRoutePoint(),new SavedRoutePoint {X=10}}};
                test.worldStore.RegisterTraveler(person);test.worldStore.RegisterTravelerVisit(visit);
                var journey=test.worldStore.TravelerJourneyFor("visit");test.worldStore.TryAdmitTraveler("visit",journey.CheckInAt);
                var savedVisit=test.worldStore.TravelerVisitsFor(1)[0];
                var plan=new TravelerPlan {Person=person,Resident=new Resident {Id=person.Id},RoomInterior=test.interior,Bed=new Bed(),
                    RoomFollower=new Follower {Arrived=blocked!=1},Visit=savedVisit,Journey=journey};
                var actor=new ResidentActor {IsSleeping=false,IsConversing=blocked==2};actor.transform.parent=test.interior.transform;
                var body=new DaggerfallEntityBehaviour();actor.Components[typeof(DaggerfallEntityBehaviour)]=body;
                actor.Components[typeof(NativeNpcVitalsBinding)]=new NativeNpcVitalsBinding {Body=body.Entity};
                actor.Components[typeof(EntityEffectManager)]=new EntityEffectManager();
                Time.unscaledTime=0;test.ObserveTravelerSleep(plan,actor,journey.Evaluate(3600),null,3600);
                Time.unscaledTime=1;test.ObserveTravelerSleep(plan,actor,journey.Evaluate(7200),null,7200);
                int expected=blocked==0 ? 20 : 10;
                if(body.Entity.CurrentHealth!=expected) throw new Exception("Standing booked-bed recovery: expected "+expected+", got "+body.Entity.CurrentHealth+"; blocked="+blocked);
            }
        }
    }
}
