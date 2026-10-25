using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Serialization;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    /// <summary>
    /// Controlled native acceptance seam for acute fear. Uses disposable ResidentActor
    /// bindings and a FullSerializer-cloned WorldStateStore; does not exercise combat sight.
    /// </summary>
    internal static class NpcAcuteFearProbe
    {
        internal const string RunFlag = "-peopleoftheiliacbay-npc-acute-fear-probe";
        private const string OwnedCharacter = "PeopleOfTheIliacBayTest-9a5f197b";
        private const string BaselineSave = "People - Gothway Garden";
        private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        internal static IEnumerator Run(PeopleOfTheIliacBayMod host, StreetPopulation streets,
            string character, string fixtureSave)
        {
            Require(Array.IndexOf(Environment.GetCommandLineArgs(), RunFlag) >= 0 &&
                character == OwnedCharacter && fixtureSave == BaselineSave && host != null && streets != null,
                "NPC acute-fear probe requires opt-in and exact owned Gothway Garden fixture.");

            WorldStateStore liveStore = Field(host, "store").GetValue(host) as WorldStateStore;
            Require(liveStore != null, "NPC acute-fear probe cannot read loaded WorldStateStore.");
            ResidentActor liveActor = null;
            float actorDeadline = Time.realtimeSinceStartup + 45f;
            while (Time.realtimeSinceStartup < actorDeadline && liveActor == null)
            {
                if (streets.Ready) liveActor = FindEligibleLiveActor(liveStore);
                if (liveActor == null) yield return new WaitForSecondsRealtime(0.25f);
            }
            Require(liveActor != null, "No eligible living managed adult resident was available after bounded actor wait.");

            bool previousStreetUpdate = streets.enabled;
            GameObject probeObject = null;
            GameObject freshProbeObject = null;
            bool witnessCasesPassed = false;
            try
            {
                // Freeze only StreetPopulation's normal writer while taking and checking
                // the live fear-record fingerprint. All writes below target a detached copy.
                streets.enabled = false;
                WorldState baseline = liveStore.Snapshot();
                FieldInfo acuteRowsField = RequireField(typeof(WorldState), "NpcAcuteFears");
                string liveFearBefore = SerializeField(acuteRowsField, baseline);

                WorldState isolatedSeed = (WorldState)SaveLoadManager.Deserialize(typeof(WorldState),
                    SaveLoadManager.Serialize(typeof(WorldState), baseline));
                Resident isolatedResident = FindResident(isolatedSeed, liveActor.Resident.Id);
                Require(isolatedResident != null && isolatedResident.IsAlive && !isolatedResident.IsQuestProtected &&
                    !ReferenceEquals(isolatedResident, liveActor.Resident),
                    "FullSerializer clone did not provide a detached living fixture resident.");
                RemoveFearForObserver(acuteRowsField, isolatedSeed, isolatedResident.Id);
                isolatedSeed.NpcPersonalities.RemoveAll(profile => profile.PersonId == isolatedResident.Id);

                var isolated = new WorldStateStore(isolatedSeed.WorldSeed);
                isolated.Restore(isolatedSeed);
                isolatedResident = isolated.ResolveResident(liveActor.Resident.Id);
                Require(isolatedResident != null && isolatedResident.IsAlive && !isolatedResident.IsQuestProtected,
                    "Isolated store cannot resolve detached fixture resident.");
                string relationshipBefore = SerializeRelationships(isolated.Snapshot());

                // A second store proves actor binding requires the exact owner reference.
                var foreignStore = new WorldStateStore(isolated.WorldSeed);
                foreignStore.Restore(isolated.Snapshot());
                probeObject = CreateBoundActor(isolated, isolatedResident);
                ResidentActor probeActor = probeObject.GetComponent<ResidentActor>();

                Require(!Observe(probeActor, foreignStore, "hostile:probe-foreign", NpcFearSeverity.Severe, 1000),
                    "Controlled actor accepted a write through a different WorldStateStore reference.");
                Require(!Observe(probeActor, isolated, "hostile:probe-inactive", NpcFearSeverity.Severe, 1000, deactivate: true),
                    "Inactive controlled ResidentActor accepted an acute-fear write.");
                isolatedResident.IsAlive = false;
                Require(!Observe(probeActor, isolated, "hostile:probe-dead", NpcFearSeverity.Severe, 1000),
                    "Dead detached resident accepted an acute-fear write.");
                isolatedResident.IsAlive = true;
                Require(isolated.ReadNpcAcuteFear(isolatedResident.Id, 1000) == null,
                    "Rejected native writes changed isolated acute-fear state.");
                Require(isolated.ReadNpcPersonality(isolatedResident.Id) == null,
                    "Rejected native writes created an unseen personality.");

                const string source = "hostile:controlled-native-fear-probe";
                Require(Observe(probeActor, isolated, source, NpcFearSeverity.Severe, 1000),
                    "Eligible controlled ResidentActor did not accept acute fear.");
                var acceptedProfile = isolated.ReadNpcPersonality(isolatedResident.Id);
                Require(acceptedProfile != null, "Accepted native witness did not save its stable personality.");
                string savedProfile = acceptedProfile.Description();
                NpcAcuteFearView first = isolated.ReadNpcAcuteFear(isolatedResident.Id, 1000);
                Require(first != null && first.CurrentFear > 0 && first.SourceId == source &&
                    first.Severity == NpcFearSeverity.Severe,
                    "Accepted observation did not create expected detached acute-fear view.");
                Require(!Observe(probeActor, isolated, source, NpcFearSeverity.Severe, 1030),
                    "Repeated native pulse inside 60 game seconds bypassed throttle.");
                Require(Observe(probeActor, isolated, source, NpcFearSeverity.Severe, 1060),
                    "Native pulse at 60 game seconds did not refresh observation.");
                NpcAcuteFearView atSixty = isolated.ReadNpcAcuteFear(isolatedResident.Id, 1060);
                Require(atSixty != null && atSixty.CurrentFear <= first.CurrentFear + 0.000001,
                    "Repeated native pulse inflated fear instead of maintaining decayed intensity.");
                Require(Observe(probeActor, isolated, source, NpcFearSeverity.Severe, 1120),
                    "Second rate-limited native refresh was not accepted.");
                NpcAcuteFearView beforeRoundTrip = isolated.ReadNpcAcuteFear(isolatedResident.Id, 1120);
                Require(beforeRoundTrip != null, "Controlled fear row vanished before FullSerializer round-trip.");

                string worldJson = SaveLoadManager.Serialize(typeof(WorldState), isolated.Snapshot());
                WorldState decoded = (WorldState)SaveLoadManager.Deserialize(typeof(WorldState), worldJson);
                Require(decoded != null, "FullSerializer returned no acute-fear world copy.");
                isolated.Restore(decoded);
                NpcAcuteFearView afterRoundTrip = isolated.ReadNpcAcuteFear(isolatedResident.Id, 1120);
                Require(SameFear(beforeRoundTrip, afterRoundTrip),
                    "FullSerializer world round-trip changed acute-fear provenance or projection.");

                double twoHoursLater = 1120 + 2 * 60 * 60;
                NpcAcuteFearView afterTwoHours = isolated.ReadNpcAcuteFear(isolatedResident.Id, twoHoursLater);
                double expectedTwoHourProjection = beforeRoundTrip.CurrentFear *
                    Math.Pow(0.5, (twoHoursLater - beforeRoundTrip.ObservedGameTime) /
                        beforeRoundTrip.RecoveryHalfLifeSeconds);
                Require(afterTwoHours != null && afterTwoHours.CurrentFear < beforeRoundTrip.CurrentFear &&
                    Math.Abs(afterTwoHours.CurrentFear - expectedTwoHourProjection) <= 1.0,
                    "Supplied +2h game-time read did not apply saved half-life projection.");
                Require(SerializeRelationships(isolated.Snapshot()) == relationshipBefore,
                    "Acute fear changed directed relationship, fear, or reliability trust ledger.");

                // Restore the isolated store from its serialized world. Old actor epoch
                // must fail; a new disposable binding to the restored store must succeed.
                isolatedResident = isolated.ResolveResident(liveActor.Resident.Id);
                Require(!Observe(probeActor, isolated, "hostile:probe-stale-epoch", NpcFearSeverity.Severe, 1180),
                    "Pre-restore ResidentActor binding remained writable after FullSerializer restore.");
                freshProbeObject = CreateBoundActor(isolated, isolatedResident);
                ResidentActor freshActor = freshProbeObject.GetComponent<ResidentActor>();
                Require(Observe(freshActor, isolated, "hostile:probe-restored-binding", NpcFearSeverity.Severe, 1180),
                    "Fresh ResidentActor binding could not write to restored isolated store.");
                Require(SerializeRelationships(isolated.Snapshot()) == relationshipBefore,
                    "Restored acute-fear write changed directed relationship or reliability trust.");
                Require(isolated.ReadNpcPersonality(isolatedResident.Id).Description() == savedProfile,
                    "Native refresh or restore changed the saved personality.");

                // Generic quest participation protects actions, not personality or
                // emotions. The production witness/action filter is tested below.
                isolatedResident.IsQuestProtected = true;
                try
                {
                    Require(Observe(freshActor, isolated, "hostile:probe-generic-quest", NpcFearSeverity.Severe, 1240),
                        "Generic quest participation incorrectly suppressed the resident's emotion.");
                    Require(isolated.ReadNpcPersonality(isolatedResident.Id).SelectedTraits.Count == 3 &&
                        isolated.ReadNpcPersonality(isolatedResident.Id).Description() == savedProfile,
                        "Generic quest participation changed the resident's three saved traits.");
                    Require(SerializeRelationships(isolated.Snapshot()) == relationshipBefore,
                        "Generic quest fear observation changed directed relationships.");
                }
                finally { isolatedResident.IsQuestProtected = false; }

                VerifyAuthoredFear(baseline, liveActor.Resident.Id);

                IEnumerator witness = RunThreatWitnessAcceptance(baseline, liveActor.Resident.Id);
                try
                {
                    // The child yields one native setup frame only. Drive it here so
                    // exceptions unwind this probe's live-writer restoration finally.
                    while (witness.MoveNext()) yield return witness.Current;
                }
                finally
                {
                    var disposableWitness = witness as IDisposable;
                    if (disposableWitness != null) disposableWitness.Dispose();
                }
                witnessCasesPassed = true;

                string liveFearAfter = SerializeField(acuteRowsField, liveStore.Snapshot());
                Require(liveFearBefore == liveFearAfter,
                    "Native acceptance probe changed live fixture acute-fear records.");
            }
            finally
            {
                streets.enabled = previousStreetUpdate;
                if (probeObject != null) UnityEngine.Object.DestroyImmediate(probeObject);
                if (freshProbeObject != null) UnityEngine.Object.DestroyImmediate(freshProbeObject);
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_ACUTE_FEAR_BASELINE_UNCHANGED: fixture=" + BaselineSave +
                "; live-acute-fear-records=equal; resident/personality/scene-writes=none; probe-objects=destroyed.");
            Require(witnessCasesPassed, "Native hostile-witness acceptance cases did not complete.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_ACUTE_FEAR_WITNESS_PASS: cases=hostile-registry,unobstructed-direct-witness,blocking-wall,out-of-range,sleeping-callback,quest-protected,inactive-actor,nonhostile-target,allied-target,dead-target; live-baseline=unchanged; player-trust=unchanged; controlled-production-filter-and-sight-path=true; real-battle-stealth-fov=not-tested; model-calls=0; service-calls=0.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_ACUTE_FEAR_NATIVE_PASS: seam=controlled-resident-actor; " +
                "isolated-world=true; accepted-pulse=true; 60s-throttle=true; supplied-plus-2h-decay=true; " +
                "relationship-and-trust-unchanged=true; fullserializer-restore=true; stale-epoch-rejected=true; " +
                "fresh-binding-accepted=true; inactive-dead-rejected=true; generic-quest-personality-retained=true; " +
                "authored-default-fear=true; unresolved-faction-rejected=true; " +
                "real-combat-los=not-tested; live-hours=not-tested; model-calls=0; service-calls=0.");
        }

        private static void VerifyAuthoredFear(WorldState baseline, string personId)
        {
            var copy = (WorldState)SaveLoadManager.Deserialize(typeof(WorldState),
                SaveLoadManager.Serialize(typeof(WorldState), baseline));
            copy.NpcAcuteFears.RemoveAll(item => item.ObserverId == personId);
            copy.NpcPersonalities.RemoveAll(item => item.PersonId == personId);
            var store = new WorldStateStore(copy.WorldSeed);
            store.Restore(copy);
            var resident = store.ResolveResident(personId);
            int individual = -1;
            foreach (var faction in GameManager.Instance.PlayerEntity.FactionData.FactionDict)
                if (faction.Value.type == (int)DaggerfallConnect.Arena2.FactionFile.FactionTypes.Individual)
                { individual = faction.Key; break; }
            Require(individual > 0, "Native authored Individual fixture unavailable.");
            // Only the detached actor receives this controlled faction identity.
            resident.NativeIdentity = new NativePerson { Id = personId, FactionId = int.MaxValue };
            var owner = CreateBoundActor(store, resident);
            string relationships = SerializeRelationships(store.Snapshot());
            try
            {
                var actor = owner.GetComponent<ResidentActor>();
                Require(!Observe(actor, store, "hostile:unknown-faction", NpcFearSeverity.Severe, 1000) &&
                    store.ReadNpcAcuteFear(personId, 1000) == null,
                    "Unresolved faction must defer native fear policy.");
                resident.NativeIdentity.FactionId = individual;
                Require(Observe(actor, store, "hostile:authored-individual", NpcFearSeverity.Severe, 1000),
                    "Authored Individual random-profile suppression incorrectly prevents witnessed fear.");
                var fear = store.ReadNpcAcuteFear(personId, 1000);
                Require(fear != null && fear.CurrentFear == 65 && Math.Abs(fear.RecoveryHalfLifeSeconds - 8280) < 0.001 &&
                    fear.SourceId == "hostile:authored-individual",
                    "Authored Individual did not use default severe fear with source provenance.");
                Require(store.ReadNpcPersonality(personId) == null &&
                    !store.Snapshot().NpcPersonalities.Exists(item => item.PersonId == personId),
                    "Authored fear recreated a random personality.");
                Require(SerializeRelationships(store.Snapshot()) == relationships,
                    "Authored witnessed fear changed directed relationships.");
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
        }

        private static IEnumerator RunThreatWitnessAcceptance(WorldState baseline, string residentId)
        {
            GameObject zone = null;
            GameObject enemyObject = null;
            GameObject wallObject = null;
            GameObject guardsObject = null;
            GameObject positiveActorObject = null;
            GameObject negativeActorObject = null;
            try
            {
                Resident positiveResident;
                int settlementId;
                WorldStateStore positiveStore = CreateWitnessStore(baseline, residentId, out positiveResident, out settlementId);
                Require(positiveResident != null && positiveResident.IsAlive && !positiveResident.IsQuestProtected,
                    "Witness fixture resident did not clone into isolated world.");

                Vector3 origin = GameManager.Instance.PlayerMotor.transform.position + Vector3.right * 500 + Vector3.up * 120;
                zone = new GameObject("Disposable isolated acute-fear witness zone");
                zone.hideFlags = HideFlags.HideAndDontSave;
                zone.transform.position = origin;
                zone.transform.rotation = Quaternion.identity;
                Physics.SyncTransforms();

                UnityEngine.Random.State randomState = UnityEngine.Random.state;
                uint nativeRandomSeed = DFRandom.Seed;
                try
                {
                    GameObject[] spawned = GameObjectHelper.CreateFoeGameObjects(origin + Vector3.up, MobileTypes.Thief, 1, MobileReactions.Passive);
                    Require(spawned != null && spawned.Length == 1 && spawned[0] != null,
                        "Native passive Thief fixture could not be created.");
                    enemyObject = spawned[0];
                }
                finally { UnityEngine.Random.state = randomState; DFRandom.Seed = nativeRandomSeed; }

                EnemyMotor motor = enemyObject.GetComponent<EnemyMotor>();
                EnemySenses senses = enemyObject.GetComponent<EnemySenses>();
                EnemyAttack attack = enemyObject.GetComponent<EnemyAttack>();
                DaggerfallEntityBehaviour target = enemyObject.GetComponent<DaggerfallEntityBehaviour>();
                Require(motor && senses && attack && target && target.Entity is EnemyEntity,
                    "Native foe fixture lacks production threat-filter components.");
                // Helper creates a passive, disabled prefab. Keep AI and attacks disabled
                // before activation, then place the real entity in the remote test zone.
                motor.IsHostile = false;
                motor.enabled = false;
                senses.enabled = false;
                attack.enabled = false;
                enemyObject.transform.SetParent(zone.transform, false);
                enemyObject.transform.localPosition = new Vector3(0, 1, 8);
                enemyObject.transform.localRotation = Quaternion.identity;
                enemyObject.SetActive(true);
                yield return null;
                motor.enabled = false;
                senses.enabled = false;
                attack.enabled = false;
                enemyObject.transform.localPosition = new Vector3(0, 1, 8);
                motor.IsHostile = true;
                Physics.SyncTransforms();

                guardsObject = new GameObject("Disposable isolated GuardPopulation threat registry");
                guardsObject.hideFlags = HideFlags.HideAndDontSave;
                guardsObject.SetActive(false);
                GuardPopulation guards = guardsObject.AddComponent<GuardPopulation>();
                guardsObject.SetActive(true);
                guards.enabled = false;
                Require(GuardPopulation.IsEnemyThreat(target),
                    "Production GuardPopulation.IsEnemyThreat rejected isolated hostile Thief.");
                string threatId = guards.RegisterHostileThreat(target);
                Require(!string.IsNullOrWhiteSpace(threatId),
                    "Production GuardPopulation.RegisterHostileThreat did not register isolated hostile Thief.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_ACUTE_FEAR_WITNESS_CASE_PASS: case=hostile-registry; expected=registered; actual=" + threatId + "; AI=disabled; isolated=true.");

                positiveActorObject = CreateBoundActor(positiveStore, positiveResident);
                PlaceWitnessActor(positiveActorObject, zone.transform);
                ResidentActor positiveActor = positiveActorObject.GetComponent<ResidentActor>();
                CivilianResponses positiveResponses = new CivilianResponses(positiveStore, settlementId);
                string positiveRelationships = SerializeRelationships(positiveStore.Snapshot());
                Require(CivilianResponses.Sees(positiveActor.transform.position, target.transform.position, 20),
                    "Remote controlled threat line was not unobstructed within 20 units.");
                positiveResponses.Threat(target, threatId, new[] { positiveActor }, 1000000, -1, ignored => false);
                CivilianIncident positiveAlarm = positiveStore.ActiveAlarm(positiveResident.Id, 1000000);
                AlarmPerson directWitness = positiveAlarm == null ? null : positiveAlarm.People.Find(person => person.ResidentId == positiveResident.Id);
                NpcAcuteFearView observedFear = positiveStore.ReadNpcAcuteFear(positiveResident.Id, 1000000);
                Require(positiveAlarm != null && positiveAlarm.ThreatId == threatId && directWitness != null && directWitness.DirectWitness,
                    "Production CivilianResponses.Threat did not record exact direct witness for registered hostile.");
                Require(observedFear != null && observedFear.SourceId == threatId && observedFear.CurrentFear > 0,
                    "Production direct-witness path did not write acute fear with registered threat provenance.");
                Require(SerializeRelationships(positiveStore.Snapshot()) == positiveRelationships,
                    "Hostile direct witness changed player trust or another directed relationship.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_ACUTE_FEAR_WITNESS_CASE_PASS: case=unobstructed-direct-witness; expected=alarm+direct-witness+acute-fear+unchanged-trust; actual=all; guard=registered; source=" + threatId + "; isolated=true.");

                wallObject = new GameObject("Disposable LOS blocker");
                wallObject.hideFlags = HideFlags.HideAndDontSave;
                wallObject.transform.SetParent(zone.transform, false);
                wallObject.transform.localPosition = new Vector3(0, 1, 4);
                BoxCollider wall = wallObject.AddComponent<BoxCollider>();
                wall.size = new Vector3(2, 3, 0.5f);
                wall.enabled = true;
                Physics.SyncTransforms();
                negativeActorObject = CreateWitnessCaseActor(baseline, residentId, zone.transform, out WorldStateStore wallStore,
                    out Resident wallResident, out int wallSettlementId);
                ResidentActor wallActor = negativeActorObject.GetComponent<ResidentActor>();
                Require(!CivilianResponses.Sees(wallActor.transform.position, target.transform.position, 20),
                    "Production CivilianResponses.Sees did not detect isolated BoxCollider wall.");
                AssertNoWitness("blocking-wall", wallStore, wallResident, wallSettlementId, target, threatId, wallActor, 1000100, ignored => false);
                wall.enabled = false;
                Physics.SyncTransforms();

                SetEnemyLocalPosition(enemyObject, new Vector3(0, 1, 21));
                Require(!CivilianResponses.Sees(wallActor.transform.position, target.transform.position, 20),
                    "Production CivilianResponses.Sees accepted hostile outside 20-unit range.");
                AssertNoWitness("out-of-range", wallStore, wallResident, wallSettlementId, target, threatId, wallActor, 1000200, ignored => false);
                SetEnemyLocalPosition(enemyObject, new Vector3(0, 1, 8));

                AssertNoWitness("sleeping-callback", wallStore, wallResident, wallSettlementId, target, threatId, wallActor, 1000300, ignored => true);

                wallResident.IsQuestProtected = true;
                AssertNoWitness("quest-protected", wallStore, wallResident, wallSettlementId, target, threatId, wallActor, 1000400, ignored => false);
                wallResident.IsQuestProtected = false;

                negativeActorObject.SetActive(false);
                AssertNoWitness("inactive-actor", wallStore, wallResident, wallSettlementId, target, threatId, wallActor, 1000500, ignored => false);
                negativeActorObject.SetActive(true);

                string relationshipBeforeRejects = SerializeRelationships(wallStore.Snapshot());
                Vector3 ignoredThreatPosition;
                motor.IsHostile = false;
                Require(!GuardPopulation.IsEnemyThreat(target) && guards.RegisterHostileThreat(target) == null,
                    "Production registry accepted nonhostile target.");
                Require(!guards.TryHostileThreatPosition(threatId, out ignoredThreatPosition),
                    "Production registry still resolved a target after it became nonhostile.");
                AssertNoWitnessWrite("nonhostile-target", wallStore, wallResident, 1000600);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_ACUTE_FEAR_WITNESS_CASE_PASS: case=nonhostile-target; expected=registry-rejected+no-witness-write; actual=all; guard=rejected.");

                motor.IsHostile = true;
                MobileTeams previousTeam = target.Entity.Team;
                target.Entity.Team = MobileTeams.PlayerAlly;
                Require(!GuardPopulation.IsEnemyThreat(target) && guards.RegisterHostileThreat(target) == null,
                    "Production registry accepted player-allied target.");
                Require(!guards.TryHostileThreatPosition(threatId, out ignoredThreatPosition),
                    "Production registry still resolved a target after it became player-allied.");
                AssertNoWitnessWrite("allied-target", wallStore, wallResident, 1000700);
                target.Entity.Team = previousTeam;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_ACUTE_FEAR_WITNESS_CASE_PASS: case=allied-target; expected=registry-rejected+no-witness-write; actual=all; guard=rejected.");

                FieldInfo currentHealth = RequireField(target.Entity.GetType(), "currentHealth");
                object originalHealth = currentHealth.GetValue(target.Entity);
                try
                {
                    // Set only the isolated entity's backing health. Public SetHealth(0)
                    // raises death events and would turn this filter probe into a combat event.
                    currentHealth.SetValue(target.Entity, 0);
                    Require(target.Entity.CurrentHealth <= 0 && !GuardPopulation.IsEnemyThreat(target) &&
                        guards.RegisterHostileThreat(target) == null,
                        "Production registry accepted entity with native current health zero.");
                    Require(!guards.TryHostileThreatPosition(threatId, out ignoredThreatPosition),
                        "Production registry still resolved a target with zero native health.");
                    AssertNoWitnessWrite("dead-target", wallStore, wallResident, 1000800);
                }
                finally { currentHealth.SetValue(target.Entity, originalHealth); }
                Require(SerializeRelationships(wallStore.Snapshot()) == relationshipBeforeRejects,
                    "Rejected hostile registry cases changed isolated player trust or directed relationships.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_ACUTE_FEAR_WITNESS_CASE_PASS: case=dead-target; expected=registry-rejected+no-witness-write; actual=all; guard=rejected; death-event=not-raised.");
            }
            finally
            {
                if (wallObject != null) UnityEngine.Object.DestroyImmediate(wallObject);
                if (positiveActorObject != null) UnityEngine.Object.DestroyImmediate(positiveActorObject);
                if (negativeActorObject != null) UnityEngine.Object.DestroyImmediate(negativeActorObject);
                if (guardsObject != null) UnityEngine.Object.DestroyImmediate(guardsObject);
                if (enemyObject != null) UnityEngine.Object.DestroyImmediate(enemyObject);
                if (zone != null) UnityEngine.Object.DestroyImmediate(zone);
                Physics.SyncTransforms();
            }
        }

        private static WorldStateStore CreateWitnessStore(WorldState baseline, string residentId,
            out Resident resident, out int settlementId)
        {
            WorldState isolatedState = (WorldState)SaveLoadManager.Deserialize(typeof(WorldState),
                SaveLoadManager.Serialize(typeof(WorldState), baseline));
            Require(isolatedState != null && isolatedState.AlarmIncidents != null,
                "FullSerializer did not clone witness fixture world.");
            resident = FindResident(isolatedState, residentId);
            Require(resident != null && resident.IsAlive && !resident.IsQuestProtected,
                "Witness fixture resident is absent or ineligible in isolated world.");
            settlementId = FindSettlementForResident(isolatedState, residentId);
            RemoveFearForObserver(RequireField(typeof(WorldState), "NpcAcuteFears"), isolatedState, residentId);
            isolatedState.NpcPersonalities.RemoveAll(profile => profile.PersonId == residentId);
            isolatedState.AlarmIncidents.Clear();
            var store = new WorldStateStore(isolatedState.WorldSeed);
            store.Restore(isolatedState);
            resident = store.ResolveResident(residentId);
            Require(resident != null && resident.IsAlive && !resident.IsQuestProtected,
                "Witness store did not resolve detached fixture resident.");
            return store;
        }

        private static int FindSettlementForResident(WorldState world, string residentId)
        {
            foreach (SettlementPopulation town in world.Settlements)
                if (town != null && town.Households != null)
                    foreach (Household household in town.Households)
                        if (household != null && household.Residents != null &&
                            household.Residents.Exists(resident => resident != null && resident.Id == residentId))
                            return town.SettlementId;
            throw new InvalidOperationException("Controlled witness resident has no household settlement.");
        }

        private static GameObject CreateWitnessCaseActor(WorldState baseline, string residentId, Transform parent,
            out WorldStateStore store, out Resident resident, out int settlementId)
        {
            store = CreateWitnessStore(baseline, residentId, out resident, out settlementId);
            GameObject actor = CreateBoundActor(store, resident);
            PlaceWitnessActor(actor, parent);
            return actor;
        }

        private static void PlaceWitnessActor(GameObject actor, Transform parent)
        {
            actor.transform.SetParent(parent, false);
            actor.transform.localPosition = new Vector3(0, 1, 0);
            actor.transform.localRotation = Quaternion.identity;
            Physics.SyncTransforms();
        }

        private static void SetEnemyLocalPosition(GameObject enemy, Vector3 localPosition)
        {
            enemy.transform.localPosition = localPosition;
            Physics.SyncTransforms();
        }

        private static void AssertNoWitness(string caseName, WorldStateStore store, Resident resident,
            int settlementId, DaggerfallEntityBehaviour target, string threatId, ResidentActor actor,
            double now, Func<string, bool> sleeping)
        {
            Require(GuardPopulation.IsEnemyThreat(target), "Valid hostile target lost registry eligibility during " + caseName + ".");
            CivilianResponses responses = new CivilianResponses(store, settlementId);
            responses.Threat(target, threatId, new[] { actor }, now, -1, sleeping);
            AssertNoWitnessWrite(caseName, store, resident, now);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_ACUTE_FEAR_WITNESS_CASE_PASS: case=" + caseName + "; expected=no-alarm+no-direct-witness+no-acute-fear; actual=all; guard=registered.");
        }

        private static void AssertNoWitnessWrite(string caseName, WorldStateStore store, Resident resident, double now)
        {
            Require(store.ActiveAlarm(resident.Id, now) == null,
                "Filtered " + caseName + " produced a witness alarm.");
            Require(store.ReadNpcAcuteFear(resident.Id, now) == null,
                "Filtered " + caseName + " wrote acute fear.");
        }

        private static ResidentActor FindEligibleLiveActor(WorldStateStore store)
        {
            ResidentActor[] actors = UnityEngine.Object.FindObjectsOfType<ResidentActor>();
            for (int index = 0; index < actors.Length; index++)
            {
                ResidentActor actor = actors[index];
                if (actor != null && actor.isActiveAndEnabled && actor.gameObject.activeInHierarchy &&
                    actor.Resident != null && actor.Resident.IsAlive && !actor.Resident.IsQuestProtected &&
                    actor.Resident.AgeGroup == ResidentAgeGroup.Adult && !actor.IsSleeping &&
                    !string.IsNullOrWhiteSpace(actor.Resident.Id) && store.ResolveResident(actor.Resident.Id) != null)
                    return actor;
            }
            return null;
        }

        private static Resident FindResident(WorldState world, string residentId)
        {
            if (world == null || world.Settlements == null) return null;
            foreach (SettlementPopulation town in world.Settlements)
                if (town != null && town.Households != null)
                    foreach (Household household in town.Households)
                        if (household != null && household.Residents != null)
                            foreach (Resident resident in household.Residents)
                                if (resident != null && string.Equals(resident.Id, residentId, StringComparison.Ordinal))
                                    return resident;
            return null;
        }

        private static GameObject CreateBoundActor(WorldStateStore store, Resident resident)
        {
            var owner = new GameObject("Disposable controlled acute-fear ResidentActor");
            owner.hideFlags = HideFlags.HideAndDontSave;
            ResidentActor actor = owner.AddComponent<ResidentActor>();
            PropertyInfo residentProperty = typeof(ResidentActor).GetProperty("Resident", InstanceMembers);
            Require(residentProperty != null && residentProperty.GetSetMethod(true) != null,
                "ResidentActor Resident binding seam missing.");
            residentProperty.GetSetMethod(true).Invoke(actor, new object[] { resident });
            Field(actor, "stateStore").SetValue(actor, store);
            Field(actor, "stateWriteEpoch").SetValue(actor, store.CaptureNpcVitalsWriteEpoch());
            return owner;
        }

        private static bool Observe(ResidentActor actor, WorldStateStore expectedStore,
            string sourceId, NpcFearSeverity severity, double now, bool deactivate = false)
        {
            MethodInfo method = typeof(ResidentActor).GetMethod("TryObserveAcuteThreatFear", InstanceMembers,
                null, new[] { typeof(WorldStateStore), typeof(string), typeof(NpcFearSeverity), typeof(double) }, null);
            if (method == null)
                throw new MissingMethodException("ResidentActor.TryObserveAcuteThreatFear(WorldStateStore, string, NpcFearSeverity, double) native acceptance seam is missing.");
            if (deactivate) actor.gameObject.SetActive(false);
            try { return (bool)method.Invoke(actor, new object[] { expectedStore, sourceId, severity, now }); }
            finally { if (deactivate) actor.gameObject.SetActive(true); }
        }

        private static bool SameFear(NpcAcuteFearView left, NpcAcuteFearView right)
        {
            return left != null && right != null && left.ObserverId == right.ObserverId &&
                left.SourceId == right.SourceId && left.Severity == right.Severity && left.Revision == right.Revision &&
                Math.Abs(left.Intensity - right.Intensity) < 0.000001 &&
                Math.Abs(left.ObservedGameTime - right.ObservedGameTime) < 0.000001 &&
                Math.Abs(left.RecoveryHalfLifeSeconds - right.RecoveryHalfLifeSeconds) < 0.000001 &&
                Math.Abs(left.CurrentFear - right.CurrentFear) < 0.000001;
        }

        private static string SerializeRelationships(WorldState state)
        { return SaveLoadManager.Serialize(typeof(NpcRelationshipLedger), state.NpcRelationships); }

        private static string SerializeField(FieldInfo field, WorldState state)
        { return SaveLoadManager.Serialize(field.FieldType, field.GetValue(state)); }

        private static void RemoveFearForObserver(FieldInfo rowsField, WorldState state, string observerId)
        {
            IList rows = rowsField.GetValue(state) as IList;
            Require(rows != null, "Isolated world has no acute-fear record list.");
            for (int index = rows.Count - 1; index >= 0; index--)
            {
                object row = rows[index];
                string rowObserver = Convert.ToString(Field(row, "ObserverId").GetValue(row),
                    System.Globalization.CultureInfo.InvariantCulture);
                if (string.Equals(rowObserver, observerId, StringComparison.Ordinal)) rows.RemoveAt(index);
            }
        }

        private static FieldInfo Field(object target, string name)
        { return Field(target.GetType(), name); }

        private static FieldInfo Field(Type type, string name)
        { return RequireField(type, name); }

        private static FieldInfo RequireField(Type type, string name)
        {
            FieldInfo field = type.GetField(name, InstanceMembers);
            if (field == null) throw new MissingFieldException(type.FullName, name);
            return field;
        }

        private static void Require(bool condition, string message)
        { if (!condition) throw new InvalidOperationException(message); }
    }
}
