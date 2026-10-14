using System;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Utility;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public sealed class ResidentActor : MonoBehaviour
    {
        public Resident Resident { get; private set; }
        public MobilePersonNPC Person { get; private set; }
        private DaggerfallEntity entity;
        private SleepingPose sleeping;
        public float NextCounterattack;
        public int Counterattacks;
        public bool IsSleeping { get { return sleeping && sleeping.Active; } }
        public void Sleep(Transform interior, BedAnchor bed, SleepingArt.Atlas atlas)
        {
            if (!sleeping) sleeping = gameObject.AddComponent<SleepingPose>();
            sleeping.Begin(this, interior, bed, atlas);
        }
        public void Initialize(Resident resident, uint surnameSeed, CityNavigation navigation)
        {
            Resident = resident;
            Person = GetComponent<MobilePersonNPC>();
            var motor = GetComponent<MobilePersonMotor>();
            motor.cityNavigation = navigation;
            motor.enabled = false;
            // Retain Motor for vanilla weapon/talk integrations, but remove its private
            // floating-origin navigation handler: the scheduled actor owns movement.
            var handler = typeof(MobilePersonMotor).GetMethod("FloatingOrigin_OnPositionUpdate", BindingFlags.NonPublic | BindingFlags.Instance);
            if (handler == null) throw new MissingMethodException("DFU mobile motor origin handler changed.");
            FloatingOrigin.OnPositionUpdate -= (FloatingOrigin.OnPositionUpdateEventHandler)
                Delegate.CreateDelegate(typeof(FloatingOrigin.OnPositionUpdateEventHandler), motor, handler);
            Person.Motor = motor;
            Person.Asset = motor.MobileAsset;
            if (resident.AgeGroup == ResidentAgeGroup.Child)
            {
                motor.MobileAsset.gameObject.SetActive(false);
                var childAsset = new GameObject("Child home sprite");
                childAsset.layer = gameObject.layer;
                childAsset.transform.SetParent(transform, false);
                Person.Asset = childAsset.AddComponent<ChildHomeBillboard>();
                motor.MobileAsset = Person.Asset;
            }
            var randomState = UnityEngine.Random.state;
            uint dfSeed = DFRandom.Seed;
            try
            {
                UnityEngine.Random.InitState(unchecked((int)resident.AppearanceSeed));
                DFRandom.Seed = resident.AppearanceSeed;
                Person.IsGuard = false;
                Person.raceToBeSet = resident.Race >= 0 && Enum.IsDefined(typeof(Races), resident.Race) ? (Races)resident.Race : Races.Breton;
                Person.genderToBeSet = resident.Sex == ResidentSex.Male ? Genders.Male : Genders.Female;
                Person.outfitVariantToBeSet = (int)((resident.AppearanceSeed >> 1) % 4);
                Person.ApplyPersonSettingsViaInspector();
                // Family surname is persisted independently of transient actors.
                var names = DaggerfallUnity.Instance.NameHelper;
                DFRandom.Seed = resident.AppearanceSeed;
                string given = names.FirstName(NameHelper.BankTypes.Breton, Person.Gender);
                DFRandom.Seed = surnameSeed;
                Person.NameNPC = resident.FullName ?? given + " " + names.Surname(NameHelper.BankTypes.Breton);
            }
            finally { UnityEngine.Random.state = randomState; DFRandom.Seed = dfSeed; }
            Person.Asset.transform.localPosition = Vector3.up * ((Person.Asset.GetSize().y - 2f) *
                (resident.AgeGroup == ResidentAgeGroup.Child ? 0.5f : 0.52f));
            entity = GetComponent<DaggerfallEntityBehaviour>().Entity;
            entity.OnDeath += Died;
        }
        private void Died(DaggerfallEntity ignored)
        {
            if (Resident != null) { Resident.IsAlive = false; if (Resident.NativeIdentity != null) Resident.NativeIdentity.IsAlive = false; }
            Debug.Log("LIVING_CITIES_RESIDENT_DEATH: " + Resident.Id);
            gameObject.SetActive(false);
        }
        private void OnDestroy() { if (entity != null) entity.OnDeath -= Died; }
        public void Place(Vector3 ground, Vector3 direction, bool standing = false)
        {
            if (sleeping) sleeping.Wake();
            transform.position = ground + Vector3.up;
            direction.y = 0;
            if (direction.sqrMagnitude > 0.00001f) transform.rotation = Quaternion.LookRotation(direction);
            bool idle = standing || GameManager.IsGamePaused;
            // DFU's setter restarts the animation even when the value is unchanged.
            if (Person.Asset.IsIdle != idle) Person.Asset.IsIdle = idle;
        }
    }
}
