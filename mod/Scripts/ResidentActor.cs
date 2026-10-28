using System;
using System.Reflection;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class ResidentActor : MonoBehaviour
    {
        private const float MaxFacingTurnRateDegreesPerSecond = 1080f;
        public Resident Resident { get; private set; }
        public double WalkingSpeed { get { return ResidentMovement.WalkSpeedFor(Resident.AppearanceSeed); } }
        public MobilePersonNPC Person { get; private set; }
        private DaggerfallEntity entity;
        private WorldStateStore stateStore;
        private object stateWriteEpoch;
        private SleepingPose sleeping;
        private SittingPose sitting;
        public bool IsSeated {get{return sitting&&sitting.Active;}}
        private void EndSitting(){if(sitting)sitting.End();SeatBodyCollision.Set(this,false);}
        private CrowdMotion crowd;
        private readonly DirectionalStandingPose standingPose = new DirectionalStandingPose();
        private Texture2D runningPilotAtlas;
        private RunningPilotPose runningPilot;
        private CompletedNpcPose completedPose;
        internal void ReleaseCompletedPose(){if(completedPose)completedPose.Release();}
        internal void ReleaseStandingForCompleteArt(){if(Person&&Person.Asset)standingPose.Set(Person.Asset,false);if(runningPilot)runningPilot.SetRunning(false);}
        internal void ShowMeleeSwing(Vector3 toward)
        {
            DaggerfallWorkshop.Game.Items.DaggerfallUnityItem weapon;
            if(!completedPose||!NativeNpcCombatAssist.TryWeapon(this,out weapon))return;
            // Selected art carries a one-handed sword, not an axe, bow or empty hand.
            int type=weapon.TemplateIndex;
            if(type>=(int)DaggerfallWorkshop.Game.Items.Weapons.Shortsword&&type<=(int)DaggerfallWorkshop.Game.Items.Weapons.Katana)
                completedPose.Swing(toward);
        }
        private bool conversing;
        private bool watchingNearbyPlayer;
        private float nextAttentionCheck;
        private bool nearbyPlayerVisible;
        public bool IsConversing { get { return conversing; } }
        public void ResumeFromSocialPause() { if(crowd) crowd.ResumeFromPause(); }
        internal void HoldPhysicalPosition()
        {
            ReleaseCompletedPose();
            if(completedPose)completedPose.Movement(false,false,Vector3.zero);
            if(!gameObject.activeInHierarchy || conversing || IsSleeping || IsSeated || !Person || !Person.Asset)return;
            ResumeFromSocialPause();SetRunningPreview(false);
            if(!standingPose.Set(Person.Asset,true) && !Person.Asset.IsIdle)Person.Asset.IsIdle=true;
        }
        private static readonly MethodInfo RefreshOrientation = typeof(MobilePersonBillboard).GetMethod("UpdateOrientation", BindingFlags.Instance | BindingFlags.NonPublic);
        public bool IsStandingStill { get { return standingPose.Active || (Person && Person.Asset && Person.Asset.IsIdle); } }
        private Action death;
        public float NextCounterattack;
        public int Counterattacks;
        public bool IsSleeping { get { var pose=sleeping?sleeping:IsStationary?GetComponent<SleepingPose>():null;return pose && pose.Active; } }
        public void Sleep(Transform interior, BedAnchor bed, SleepingArt.Atlas atlas)
        {
            ReleaseCompletedPose();
            if(completedPose)completedPose.Movement(false,false,Vector3.zero);
            EndSitting();
            SetRunningPreview(false);
            if(Person && Person.Asset)standingPose.Set(Person.Asset, false);
            if(crowd) crowd.ResetPlacement();
            if (!sleeping) sleeping = gameObject.AddComponent<SleepingPose>();
            if(IsStationary)sleeping.BeginNative(interior,bed,atlas);
            else sleeping.Begin(this, interior, bed, atlas);
        }
        public void Initialize(Resident resident, uint surnameSeed, CityNavigation navigation,Action onDeath=null,WorldStateStore inventoryStore=null)
        {
            death=onDeath;
            Resident = resident;
            stateStore=inventoryStore;
            stateWriteEpoch=inventoryStore==null?null:inventoryStore.CaptureNpcVitalsWriteEpoch();
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
                long appearanceStarted=RuntimeTiming.Start("resident-native-appearance");
                try { Person.ApplyPersonSettingsViaInspector(); }
                finally { RuntimeTiming.End("resident-native-appearance",appearanceStarted); }
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
            if(inventoryStore!=null && !resident.IsQuestProtected)
            {
                long inventoryStarted=RuntimeTiming.Start("resident-inventory-bind");
                try { gameObject.AddComponent<NativeNpcInventoryOwner>().Bind(entity,inventoryStore,resident); }
                finally { RuntimeTiming.End("resident-inventory-bind",inventoryStarted); }
            }
            NativeNpcVitalsBinding.BindTo(gameObject,entity,inventoryStore,resident);
            NativeNpcPoisonOnsetBinding.BindTo(gameObject,inventoryStore,resident);
            entity.OnDeath += Died;
            completedPose=CompletedNpcPose.Bind(this);
        }
        // Called only after the native hostile-threat witness has passed its sight checks.
        // A restored world invalidates this actor's write authority even if its person ID survives.
        internal bool TryObserveAcuteThreatFear(WorldStateStore expectedStore, string sourceId,
            NpcFearSeverity severity, double now)
        {
            if (stateStore == null || !ReferenceEquals(stateStore, expectedStore) ||
                !stateStore.IsNpcVitalsWriteEpochCurrent(stateWriteEpoch) ||
                Resident == null || !Resident.IsAlive || IsSleeping ||
                !isActiveAndEnabled || !gameObject.activeInHierarchy ||
                double.IsNaN(now) || double.IsInfinity(now) || now < 0 ||
                string.IsNullOrWhiteSpace(sourceId) || sourceId.Length > 512 ||
                !Enum.IsDefined(typeof(NpcFearSeverity), severity)) return false;
            for (int i = 0; i < sourceId.Length; i++) if (char.IsControl(sourceId[i])) return false;
            Resident current = stateStore.ResolveResident(Resident.Id);
            if (current == null || !current.IsAlive) return false;
            // Check the clock before creating any previously unseen profile.
            try { stateStore.ReadNpcAcuteFear(Resident.Id, now); }
            catch (ArgumentOutOfRangeException) { return false; }
            NpcSocialBackground background;
            bool randomEligible = PeopleOfTheIliacBayMod.RefreshSavedRandomPersonalityEligibility(
                stateStore, Resident.Id, out background);
            if (!randomEligible)
            {
                int factionId;
                // Authored identity suppresses random traits, not emotion. A known
                // Individual uses the Core default fear policy until authored traits exist.
                // Unresolved faction data still defers the observation.
                if (!stateStore.TryReadNpcPersonalityFaction(Resident.Id, out factionId) ||
                    !PeopleOfTheIliacBayMod.IsAuthoredNativeIndividual(factionId)) return false;
            }
            if (randomEligible && stateStore.ReadNpcPersonality(Resident.Id) == null)
            {
                stateStore.GetOrCreateNpcPersonality(current.Id, current.AppearanceSeed, background);
            }
            return stateStore.TryObserveNpcAcuteFear(Resident.Id, sourceId, severity, now);
        }
        public void CaptureInventory()
        {
            if(stateStore!=null && !stateStore.IsNpcVitalsWriteEpochCurrent(stateWriteEpoch))return;
            var owner=GetComponent<NativeNpcInventoryOwner>();
            if(owner) owner.Capture();
        }
        public void CaptureState()
        {
            if(stateStore!=null && !stateStore.IsNpcVitalsWriteEpochCurrent(stateWriteEpoch))return;
            CaptureInventory();
            var vitals=GetComponent<NativeNpcVitalsBinding>();
            if(vitals)vitals.Capture();
        }
        private void Died(DaggerfallEntity ignored)
        {
            EndSitting();
            CaptureInventory();
            SetRunningPreview(false);
            if(stateStore!=null && Resident!=null && stateStore.IsNpcVitalsWriteEpochCurrent(stateWriteEpoch))
            {
                var traveler=stateStore.Traveler(Resident.Id);
                if(traveler!=null && traveler.HomeKey>=0 && ReferenceEquals(stateStore.TravelerOriginResident(Resident.Id),Resident))
                    stateStore.KillTraveler(Resident.Id,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
            }
            if(death!=null) death();
            if (Resident != null) { Resident.IsAlive = false; if (Resident.NativeIdentity != null) Resident.NativeIdentity.IsAlive = false; }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_RESIDENT_DEATH: " + Resident.Id);
            gameObject.SetActive(false);
        }
        private void OnDisable() { ReleaseCompletedPose();EndSitting();SetRunningPreview(false);if(Person)standingPose.Set(Person.Asset,false); }
        private void OnDestroy() { EndSitting();SetRunningPreview(false); if (entity != null) entity.OnDeath -= Died; }
        public void SetRunningPilotAtlas(Texture2D atlas)
        {
            if (runningPilotAtlas == atlas) return;
            SetRunningPreview(false);
            runningPilotAtlas = atlas;
        }
        public void TransferOwner(Transform parent,CityNavigation navigation)
        {
            transform.SetParent(parent,true);
            if(Person && Person.Motor)Person.Motor.cityNavigation=navigation;
        }
        public bool SetRunningPreview(bool fleeing)
        {
            bool eligible=fleeing&&Resident!=null&&Resident.IsAlive&&Resident.AgeGroup==ResidentAgeGroup.Adult&&
                !conversing&&!IsSleeping&&!IsSeated&&!GameManager.IsGamePaused&&!standingPose.Active&&
                (IsStationary||Person&&Person.Asset&&Person.Asset.gameObject.activeInHierarchy&&!Person.Asset.IsIdle);
            if(completedPose)completedPose.Running(eligible);
            if(eligible&&CompletedNpcArt.Current!=null&&CompletedNpcArt.Current.ForActor(this,"run")!=null)
            {if(runningPilot)runningPilot.SetRunning(false);return true;}
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
            ReleaseCompletedPose();
            EndSitting();
            if(Person && Person.Asset)standingPose.Set(Person.Asset, false);
            var camera = GameManager.Instance.MainCamera;
            if (camera)
            {
                var towardPlayer = camera.transform.position - transform.position;
                towardPlayer.y = 0;
                if (towardPlayer.sqrMagnitude > 0.00001f) transform.rotation = Quaternion.LookRotation(towardPlayer);
            }
            if (Person && Person.Asset && !Person.Asset.IsIdle)
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
        public void Place(Vector3 ground, Vector3 direction, bool standing = false, bool fleeing = false,bool waitAtOccupiedTarget=false,SelectedSeatAccess seatAccess=null,bool freeStanding=false,bool comfortWalking=true,Func<Vector3,bool> placementAllowed=null,Vector3? navigationDestination=null,bool preferNative=true)
        {
            ReleaseCompletedPose();
            if(fleeing)ReleaseFlightPause();
            if(!fleeing && HoldGuardInquiryPosition())return;
            var jump=GetComponent<NativeDungeonJumpTraversal>();
            if(!fleeing && jump && jump.Active){jump.Advance();return;}
            bool seatTarget=seatAccess!=null&&seatAccess.Valid&&standing&&(ground-seatAccess.Slot.Ground).sqrMagnitude<0.0001f;
            if(!seatTarget)EndSitting();
            if(seatAccess!=null && seatAccess.Valid && seatAccess.Permits(transform.position-Vector3.up,ground))SeatBodyCollision.Set(this,true);
            if (!fleeing || standing || conversing) SetRunningPreview(false);
            if (conversing) { var stopped=GetComponent<NativeResidentNavigation>();if(stopped)stopped.Pause();FaceConversation(); return; }
            if (sleeping) sleeping.Wake();
            var previousFacing=transform.rotation;
            var before=transform.position;
            Vector3 actual;
            bool nativeMoved=false;
            var native=GetComponent<NativeResidentNavigation>();
            if(!IsStationary && preferNative && !fleeing && seatAccess==null && NativeNavigationWorld.Current && NativeNavigationWorld.Current.Ready)
            {
                if(!native)native=gameObject.AddComponent<NativeResidentNavigation>();
                nativeMoved=native.TryMove(navigationDestination.HasValue && crowd && crowd.HasPlacement?
                    transform.position-Vector3.up:ground,
                    navigationDestination??ground,WalkingSpeed,placementAllowed);
            }
            else if(native)native.Pause();
            if(!nativeMoved)
            {
                // A resumed physical flee/report route must not inherit the
                // ordinary walking cap from initial body configuration.
                if(crowd && Resident!=null)crowd.Configure(1,fleeing&&!standing?ResidentMovement.FleeSpeed(Resident.AppearanceSeed):WalkingSpeed);
                if(crowd && !crowd.Place(ground,out actual,comfortWalking&&!standing,waitAtOccupiedTarget,seatAccess,placementAllowed)) { gameObject.SetActive(false); return; }
                if(!crowd) transform.position=ground+BodyOffset;
            }
            if(!gameObject.activeSelf) gameObject.SetActive(true);
            var moved=transform.position-before;
            bool atActivity = standing && (moved.sqrMagnitude<0.000001f ||
                Vector3.Distance(transform.position,ground+Vector3.up)<0.15f);
            // Crowd separation can nudge a stopped browser sideways on successive
            // frames. Keep the activity target authoritative while occupied;
            // locomotion still follows actual displacement.
            if(!atActivity && moved.sqrMagnitude>0.000001f && moved.sqrMagnitude<1) direction=moved;
            direction.y = 0;
            if (direction.sqrMagnitude > 0.00001f && (standing || moved.sqrMagnitude>0.000001f))
            {
                var desired=Quaternion.LookRotation(direction);
                bool turnWhileWalking=!atActivity && !GameManager.IsGamePaused && moved.sqrMagnitude>0.000001f;
                double heading=WalkingHeading.StepDegrees(transform.eulerAngles.y,desired.eulerAngles.y,8,MaxFacingTurnRateDegreesPerSecond,
                    Time.unscaledDeltaTime,turnWhileWalking);
                transform.rotation=Quaternion.Euler(0,(float)heading,0);
            }
            bool idle = GameManager.IsGamePaused || moved.sqrMagnitude<0.000001f ||
                atActivity;
            if(freeStanding&&idle&&!seatTarget&&!fleeing)
            {
                transform.rotation=previousFacing;
                WatchNearbyPlayer();
            }
            else {watchingNearbyPlayer=false;nearbyPlayerVisible=false;nextAttentionCheck=0;}
            // DFU's setter restarts the animation even when the value is unchanged.
            // A stopped walker retains its world heading. Optional native-scale
            // standing art supplies all bearings; unknown assets keep the fallback.
            if(completedPose)completedPose.Movement(!idle,fleeing&&!idle,direction);
            if(IsStationary)return;
            if (!standingPose.Set(Person.Asset, atActivity || idle) && Person.Asset.IsIdle != idle)
                Person.Asset.IsIdle = idle;
            SetRunningPreview(fleeing && !standing && !idle);
            if(seatTarget&&Vector3.Distance(transform.position-Vector3.up,seatAccess.Slot.Ground)<0.10f)
            {
                transform.rotation=Quaternion.LookRotation(seatAccess.Slot.Facing);
                var art=SittingArt.Current==null?null:SittingArt.Current.ForActor(this);
                if(art!=null)
                {
                    standingPose.Set(Person.Asset,false);
                    if(!sitting)sitting=gameObject.AddComponent<SittingPose>();
                    if(!sitting.Begin(this,seatAccess,art))EndSitting();
                }
                else EndSitting();
            }
            else EndSitting();
            // An approach is not yet a seated pose. Ending the visual pose must
            // not re-enable the body during the reserved final movement step.
            if(seatAccess!=null && seatAccess.Valid && seatAccess.Permits(transform.position-Vector3.up,ground))SeatBodyCollision.Set(this,true);
        }
        private void WatchNearbyPlayer()
        {
            if(GameManager.IsGamePaused)return;
            var game=GameManager.Instance;var camera=game.MainCamera;if(!camera)return;
            var delta=camera.transform.position-transform.position;delta.y=0;
            float range=watchingNearbyPlayer?3f:2.5f;
            if(delta.sqrMagnitude>range*range||delta.sqrMagnitude<0.0001f||game.PlayerEntity.IsMagicallyConcealed)
            {watchingNearbyPlayer=false;nearbyPlayerVisible=false;return;}
            // At most four short visibility checks per second, only for nearby
            // idle residents. Active activities never enter this path.
            if(Time.unscaledTime>=nextAttentionCheck)
            {
                nextAttentionCheck=Time.unscaledTime+0.25f;
                nearbyPlayerVisible=InteriorActivitySight.Visible(transform.position+Vector3.up*0.5f,camera.transform.position,game.PlayerMotor.transform);
            }
            watchingNearbyPlayer=nearbyPlayerVisible;
            if(watchingNearbyPlayer)transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(delta),MaxFacingTurnRateDegreesPerSecond*Time.unscaledDeltaTime);
        }
    }
}
