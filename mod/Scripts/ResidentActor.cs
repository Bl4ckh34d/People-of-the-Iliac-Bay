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
            var randomState = UnityEngine.Random.state;
            uint dfSeed = DFRandom.Seed;
            try
            {
                UnityEngine.Random.InitState(unchecked((int)resident.AppearanceSeed));
                DFRandom.Seed = resident.AppearanceSeed;
                Person.IsGuard = false;
                Person.raceToBeSet = Races.Breton;
                Person.genderToBeSet = (resident.AppearanceSeed & 1) == 0 ? Genders.Male : Genders.Female;
                Person.outfitVariantToBeSet = (int)((resident.AppearanceSeed >> 1) % 4);
                Person.ApplyPersonSettingsViaInspector();
                // Regional surname conventions and persisted literal names join the
                // family schema later. This first Breton town uses stable shared names.
                var names = DaggerfallUnity.Instance.NameHelper;
                DFRandom.Seed = resident.AppearanceSeed;
                string given = names.FirstName(NameHelper.BankTypes.Breton, Person.Gender);
                DFRandom.Seed = surnameSeed;
                Person.NameNPC = given + " " + names.Surname(NameHelper.BankTypes.Breton);
            }
            finally { UnityEngine.Random.state = randomState; DFRandom.Seed = dfSeed; }
            Person.Asset.transform.localPosition = Vector3.up * ((Person.Asset.GetSize().y - 2f) * 0.52f);
            entity = GetComponent<DaggerfallEntityBehaviour>().Entity;
            entity.OnDeath += Died;
        }
        private void Died(DaggerfallEntity ignored)
        {
            if (Resident != null) Resident.IsAlive = false;
            Debug.Log("LIVING_CITIES_RESIDENT_DEATH: " + Resident.Id);
            gameObject.SetActive(false);
        }
        private void OnDestroy() { if (entity != null) entity.OnDeath -= Died; }
        public void Place(Vector3 ground, Vector3 direction, bool standing = false)
        {
            transform.position = ground + Vector3.up;
            direction.y = 0;
            if (direction.sqrMagnitude > 0.00001f) transform.rotation = Quaternion.LookRotation(direction);
            bool idle = standing || GameManager.IsGamePaused;
            // DFU's setter restarts the animation even when the value is unchanged.
            if (Person.Asset.IsIdle != idle) Person.Asset.IsIdle = idle;
        }
    }
}
