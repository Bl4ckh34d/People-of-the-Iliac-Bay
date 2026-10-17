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
        public double WalkingSpeed { get { return ResidentMovement.WalkSpeedFor(Resident.AppearanceSeed); } }
        public MobilePersonNPC Person { get; private set; }
        private DaggerfallEntity entity;
        private SleepingPose sleeping;
        private CrowdMotion crowd;
        private readonly DirectionalStandingPose standingPose = new DirectionalStandingPose();
        private Texture2D runningPilotAtlas;
        private RunningPilotPose runningPilot;
        private bool conversing;
        public bool IsConversing { get { return conversing; } }
        public void ResumeFromSocialPause() { if(crowd) crowd.ResumeFromPause(); }
        private static readonly MethodInfo RefreshOrientation = typeof(MobilePersonBillboard).GetMethod("UpdateOrientation", BindingFlags.Instance | BindingFlags.NonPublic);
        public bool IsStandingStill { get { return standingPose.Active || (Person && Person.Asset && Person.Asset.IsIdle); } }
        private Action death;
        public float NextCounterattack;
        public int Counterattacks;
        public bool IsSleeping { get { return sleeping && sleeping.Active; } }
        public void Sleep(Transform interior, BedAnchor bed, SleepingArt.Atlas atlas)
        {
            SetRunningPreview(false);
            standingPose.Set(Person.Asset, false);
            if(crowd) crowd.ResetPlacement();
            if (!sleeping) sleeping = gameObject.AddComponent<SleepingPose>();
            sleeping.Begin(this, interior, bed, atlas);
        }
        public void Initialize(Resident resident, uint surnameSeed, CityNavigation navigation,Action onDeath=null)
        {
            death=onDeath;
            Resident = resident;
            crowd=GetComponent<CrowdMotion>(); if(!crowd) crowd=gameObject.AddComponent<CrowdMotion>(); crowd.Configure(1,WalkingSpeed);
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
            // Keep native interaction body consistent with the steering footprint.
            var capsule=GetComponent<CapsuleCollider>();
            if(capsule) capsule.radius=CrowdMotion.BodyRadius;
            entity = GetComponent<DaggerfallEntityBehaviour>().Entity;
            entity.OnDeath += Died;
        }
        private void Died(DaggerfallEntity ignored)
        {
            SetRunningPreview(false);
            if(death!=null) death();
            if (Resident != null) { Resident.IsAlive = false; if (Resident.NativeIdentity != null) Resident.NativeIdentity.IsAlive = false; }
            Debug.Log("LIVING_CITIES_RESIDENT_DEATH: " + Resident.Id);
            gameObject.SetActive(false);
        }
        private void OnDisable() { SetRunningPreview(false); }
        private void OnDestroy() { SetRunningPreview(false); if (entity != null) entity.OnDeath -= Died; }
        public void SetRunningPilotAtlas(Texture2D atlas)
        {
            if (runningPilotAtlas == atlas) return;
            SetRunningPreview(false);
            runningPilotAtlas = atlas;
        }
        public void TransferOwner(Transform parent,CityNavigation navigation)
        {
            transform.SetParent(parent,true);
            Person.Motor.cityNavigation=navigation;
        }
        public bool SetRunningPreview(bool fleeing)
        {
            if (!fleeing || !CanShowRunningPreview())
            {
                if (runningPilot) runningPilot.SetRunning(false);
                return false;
            }
            if (!runningPilot) runningPilot = gameObject.AddComponent<RunningPilotPose>();
            if (!runningPilot.Configure(Person.Asset, 385, runningPilotAtlas))
            {
                runningPilot.SetRunning(false);
                return false;
            }
            runningPilot.SetRunning(true);
            return runningPilot.Active;
        }
        private bool CanShowRunningPreview()
        {
            return runningPilotAtlas && Resident != null && Resident.IsAlive &&
                Resident.AgeGroup == ResidentAgeGroup.Adult && !conversing && !IsSleeping &&
                !GameManager.IsGamePaused && !standingPose.Active && Person && !Person.IsGuard &&
                Person.Race == Races.Breton && Person.Gender == Genders.Male &&
                Person.PersonOutfitVariant == 0 && Person.Asset is MobilePersonBillboard &&
                Person.Asset.gameObject.activeInHierarchy && !Person.Asset.IsIdle;
        }
        public void SetConversationFacing(bool active)
        {
            SetRunningPreview(false);
            conversing = active;
            if (active) FaceConversation();
        }
        private void FaceConversation()
        {
            standingPose.Set(Person.Asset, false);
            var camera = GameManager.Instance.MainCamera;
            if (camera)
            {
                var towardPlayer = camera.transform.position - transform.position;
                towardPlayer.y = 0;
                if (towardPlayer.sqrMagnitude > 0.00001f) transform.rotation = Quaternion.LookRotation(towardPlayer);
            }
            if (!Person.Asset.IsIdle)
            {
                Person.Asset.IsIdle = true;
                var native = Person.Asset as MobilePersonBillboard;
                if (native)
                {
                    if (RefreshOrientation == null) throw new MissingMethodException("DFU mobile orientation method changed.");
                    RefreshOrientation.Invoke(native, null);
                }
            }
        }
        public void Place(Vector3 ground, Vector3 direction, bool standing = false, bool fleeing = false,bool waitAtOccupiedTarget=false)
        {
            if (!fleeing || standing || conversing) SetRunningPreview(false);
            if (conversing) { FaceConversation(); return; }
            if (sleeping) sleeping.Wake();
            var before=transform.position;
            Vector3 actual;
            if(crowd && !crowd.Place(ground,out actual,!standing,waitAtOccupiedTarget)) { gameObject.SetActive(false); return; }
            if(!crowd) transform.position=ground+Vector3.up;
            if(!gameObject.activeSelf) gameObject.SetActive(true);
            var moved=transform.position-before;
            bool atActivity = standing && (moved.sqrMagnitude<0.000001f ||
                Vector3.Distance(transform.position,ground+Vector3.up)<0.15f);
            // Crowd separation can nudge a stopped browser sideways on successive
            // frames. Keep the activity target authoritative while occupied;
            // locomotion still follows actual displacement.
            if(!atActivity && moved.sqrMagnitude>0.000001f && moved.sqrMagnitude<1) direction=moved;
            direction.y = 0;
            if (direction.sqrMagnitude > 0.00001f && (standing || moved.sqrMagnitude>0.000001f)) transform.rotation = Quaternion.LookRotation(direction);
            bool idle = GameManager.IsGamePaused || moved.sqrMagnitude<0.000001f ||
                atActivity;
            // DFU's setter restarts the animation even when the value is unchanged.
            // Native idle has only front-facing art. A stopped walker still has
            // a world heading; preserve it with a held directional walk frame.
            if (!standingPose.Set(Person.Asset, atActivity || idle) && Person.Asset.IsIdle != idle)
                Person.Asset.IsIdle = idle;
            SetRunningPreview(fleeing && !standing && !atActivity);
        }
    }
}
