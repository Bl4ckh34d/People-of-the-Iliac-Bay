using System.Collections.Generic;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Questing;
using DaggerfallWorkshop.Game.Utility;
using DaggerfallWorkshop.Utility;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public sealed partial class StreetPopulation
    {
        private System.IDisposable keeperServiceHook;
        private sealed class KeeperView
        {
            public StaticNPC Npc;
            public Vector3 Position, Offset;
            public bool WasActive;
        }
        private readonly Dictionary<string, KeeperView> keeperViews = new Dictionary<string, KeeperView>();
        private readonly Dictionary<string, DailySchedule> keeperSchedules = new Dictionary<string, DailySchedule>();
        private DailySchedule KeeperSchedule(NativePerson person)
        {
            if (person.HomeKey != person.WorkKey || person.BedSlot < 0 || !Ready || !directory) return null;
            DailySchedule schedule;
            if (keeperSchedules.TryGetValue(person.Id, out schedule)) return schedule;
            BuildingSummary summary;
            if (!directory.GetBuildingSummary(person.WorkKey, out summary) || !RMBLayout.IsShop(summary.BuildingType)) return null;
            int type = (int)summary.BuildingType;
            int opens = PlayerActivate.openHours[type] * 60, closes = PlayerActivate.closeHours[type] * 60;
            if (closes >= 1440 || closes <= opens) return null;
            uint seed = StableSeed.Combine(0, (uint)person.SettlementId, (uint)person.WorkKey, unchecked((uint)person.NativeHash));
            int wake = Mathf.Clamp(wakeHour * 60 + (int)(seed % 31) - 15, 0, Mathf.Max(0, opens - 10 - (int)(seed % 21)));
            int sleep = Mathf.Clamp(sleepHour * 60 + (int)((seed >> 8) % 31) - 15, Mathf.Min(1439, closes + 10 + (int)((seed >> 8) % 21)), 1439);
            schedule = DailySchedule.Workday(person.HomeKey, person.WorkKey, new RoutePoint[0], opens, closes, 0, wake, sleep);
            keeperSchedules.Add(person.Id, schedule); return schedule;
        }
        private bool KeeperSleeping(NativePerson person, double now)
        {
            var schedule = KeeperSchedule(person);
            return schedule != null && schedule.EvaluateSeconds(now).Activity == Activity.Sleep &&
                (!ReactionsEnabled || worldStore.NativeResponse(person.Id) == null);
        }
        private void UpdateKeeperSleep(StaticNPC npc, NativePerson person, double now, bool alarmed)
        {
            var pose = npc.GetComponent<SleepingPose>();
            var art = SleepEnabled && !alarmed && person.IsAlive && person.HomeKey == person.WorkKey &&
                KeeperSleeping(person, now) && sleepingArt != null ? sleepingArt.ForNative(person) : null;
            if (art == null) { if (pose) pose.Wake(); return; }
            var records = new List<int>(indoorBeds.Keys); records.Sort();
            if (person.BedSlot < 0 || person.BedSlot >= records.Count) { if (pose) pose.Wake(); return; }
            if (!pose) pose = npc.gameObject.AddComponent<SleepingPose>();
            pose.BeginNative(interior.transform, indoorBeds[records[person.BedSlot]], art);
        }
        private void InstallKeeperServiceHook()
        {
            var type = System.Type.GetType("LivingCities.QuestHooks.NativeServiceHooks, LivingCities.QuestHooks", true);
            keeperServiceHook = (System.IDisposable)System.Activator.CreateInstance(type,
                new object[] { new System.Func<StaticNPC, bool>(KeeperServiceBlocked) });
        }
        public bool KeeperServiceBlocked(StaticNPC npc)
        {
            var person = KeeperPerson(npc);
            return person != null && (!person.IsAlive || KeeperSleeping(person, DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()) || ReactionsEnabled &&
                worldStore.NativeResponse(person.Id) != null);
        }
        private NativePerson KeeperPerson(StaticNPC npc)
        {
            if (!npc || !interior || worldStore == null || population == null ||
                npc.GetComponent<QuestResourceBehaviour>() || npc.GetComponent<DaggerfallAction>() ||
                npc.Data.context != StaticNPC.Context.Building || npc.Data.buildingKey != interior.EntryDoor.buildingKey) return null;
            return worldStore.NativePeopleFor(population.SettlementId).Find(p => p.WorkKey == npc.Data.buildingKey && p.NativeHash == npc.Data.hash);
        }
        private System.Action<System.Exception> BeginKeeperHit(Transform target)
        {
            if (!ReactionsEnabled || !Ready || !target || !interior || responses == null) return null;
            // DFU's static merchant flats have no entity damage branch. Retain native
            // damage ownership if another mod has supplied an entity on this target.
            if (target.GetComponent<DaggerfallWorkshop.Game.Entity.DaggerfallEntityBehaviour>()) return null;
            SurveyKeeperIdentities();
            var npc = target.GetComponent<StaticNPC>(); var keeper = KeeperPerson(npc);
            if (keeper == null || !keeper.IsAlive || !npc.gameObject.activeInHierarchy) return null;
            var family = population.Households.Find(h => h.Residents.Exists(r => r.Id == keeper.Id));
            var member = family == null ? null : family.Residents.Find(r => r.Id == keeper.Id);
            if (member != null && member.IsQuestProtected) return null;
            var room = interior; int building = room.EntryDoor.buildingKey;
            var billboard = npc.GetComponent<DaggerfallBillboard>();
            var sleeping = npc.GetComponent<SleepingPose>();
            var ground = sleeping && sleeping.Active ? npc.transform.position :
                npc.transform.position - Vector3.up * (billboard ? billboard.Summary.Size.y * 0.5f : 1);
            var local = room.transform.InverseTransformPoint(ground);
            return error => {
                if (error != null || !keeper.IsAlive) return;
                double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                var alarm = worldStore.RecordViolence(population.SettlementId, keeper.Id, now);
                if (!alarm.HasLocation) worldStore.LocateViolence(alarm.Id, building, local.x, local.y, local.z);
                foreach (var actor in actors.Values)
                {
                    if (!actor || !actor.Resident.IsAlive || !actor.gameObject.activeInHierarchy ||
                        GetState(actor.Resident.Id, now).Activity == Activity.Sleep ||
                        !CivilianResponses.Sees(actor.transform.position, npc.transform.position, 20)) continue;
                    var witness = worldStore.Witness(alarm.Id, actor.Resident.Id, now);
                    var point = room.transform.InverseTransformPoint(actor.transform.position - Vector3.up);
                    witness.HasPosition = true; witness.BuildingKey = building;
                    witness.X = point.x; witness.Y = point.y; witness.Z = point.z;
                }
                ObserveKeeperViolence(alarm, npc.transform.position, now);
                // Match native mobile civilian lethality. Combat health/capability and
                // matching battle art are separate work, not supplied by this flat.
                if (member != null) Population.MarkDead(population, keeper.Id);
                keeper.IsAlive = false;
                npc.gameObject.SetActive(false);
                KeeperView view;
                if (keeperViews.TryGetValue(keeper.Id, out view)) view.WasActive = false;
                worldStore.ConfirmCorpse(alarm.Id, now, building, local.x, local.y, local.z);
                var guards = GetComponent<GuardPopulation>();
                var player = GameManager.Instance.PlayerEntity;
                player.TallyCrimeGuildRequirements(false, 5);
                bool concealed = alarm.People.Count == 0 && indoorGeometry != null && indoorGeometry.EntryGround.HasValue &&
                    guards && guards.CanInvestigate(building);
                if (concealed) worldStore.RegisterUnwitnessedScene(alarm.Id);
                else player.CrimeCommitted = DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.Murder;
                PrepareIndoorEscapes(now);
                // Finite guards learn through the existing physical witness/discovery
                // pipeline. Explicitly disabled finite guards retain native reporting.
                if (!guards || !guards.OwnsSettlement) player.SpawnCityGuards(true);
                Debug.Log("LIVING_CITIES_KEEPER_DEATH: " + keeper.Id + "; witnesses=" + alarm.People.Count + "; concealed=" + concealed);
            };
        }
        public void ObserveKeeperViolence(CivilianIncident alarm, Vector3 victim, double now)
        {
            if (!ReactionsEnabled || !interior || alarm == null) return;
            SurveyKeeperIdentities();
            foreach (var npc in interior.GetComponentsInChildren<StaticNPC>())
            {
                var person = KeeperPerson(npc);
                if (person == null || !person.IsAlive || KeeperSleeping(person, now) || !CivilianResponses.Sees(npc.transform.position, victim, 20)) continue;
                var witness = worldStore.Witness(alarm.Id, person.Id, now);
                if (witness == null || witness.HasPosition) continue;
                var billboard = npc.GetComponent<DaggerfallBillboard>();
                var offset = Vector3.up * (billboard ? billboard.Summary.Size.y * 0.5f : 1);
                var local = interior.transform.InverseTransformPoint(npc.transform.position - offset);
                witness.HasPosition = true; witness.BuildingKey = interior.EntryDoor.buildingKey;
                witness.X = local.x; witness.Y = local.y; witness.Z = local.z;
            }
        }
        private void UpdateKeeperResponses(double now)
        {
            if (!interior || worldStore == null || population == null) return;
            foreach (var npc in interior.GetComponentsInChildren<StaticNPC>(true))
            {
                var person = KeeperPerson(npc);
                if (person == null) continue;
                KeeperView view;
                UpdateKeeperSleep(npc, person, now, ReactionsEnabled && worldStore.NativeResponse(person.Id) != null);
                if (!person.IsAlive)
                {
                    npc.gameObject.SetActive(false);
                    if (keeperViews.TryGetValue(person.Id, out view)) view.WasActive = false;
                    continue;
                }
                var alarm = ReactionsEnabled ? worldStore.NativeResponse(person.Id) : null;
                if (alarm != null && GameManager.Instance.PlayerEntity.CrimeCommitted == DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.None &&
                    worldStore.ClearNativeResponse(person.Id, now)) alarm = null;
                if (alarm == null)
                {
                    if (keeperViews.TryGetValue(person.Id, out view))
                    { RestoreKeeperView(view); keeperViews.Remove(person.Id); }
                    continue;
                }
                if (!keeperViews.TryGetValue(person.Id, out view))
                {
                    var billboard = npc.GetComponent<DaggerfallBillboard>();
                    view = new KeeperView { Npc = npc, Position = npc.transform.position,
                        Offset = Vector3.up * (billboard ? billboard.Summary.Size.y * 0.5f : 1), WasActive = npc.gameObject.activeSelf };
                    keeperViews.Add(person.Id, view);
                }
                var witness = alarm.People.Find(p => p.ResidentId == person.Id);
                if (!witness.HasPosition || witness.Reaction != CivilianReaction.Flee) continue;
                if (witness.Escape == null && !GameManager.IsGamePaused && indoorGeometry != null)
                {
                    var guards = GetComponent<GuardPopulation>();
                    var outside = guards ? guards.WitnessRoute(interior.EntryDoor.buildingKey, now) : null;
                    var inside = indoorGeometry.RouteToExit(interior.transform.TransformPoint(new Vector3((float)witness.X, (float)witness.Y, (float)witness.Z)));
                    if (outside != null && inside.Count >= 2)
                        witness.Escape = CivilianEscape.Create(witness, now, inside, outside, ResidentMovement.FleeSpeed(
                            StableSeed.Combine(0, (uint)person.SettlementId, (uint)person.WorkKey, unchecked((uint)person.NativeHash))));
                }
                if (witness.Escape == null) continue;
                if (witness.BuildingKey == interior.EntryDoor.buildingKey)
                {
                    var ground = interior.transform.TransformPoint(new Vector3((float)witness.X, (float)witness.Y, (float)witness.Z));
                    InteriorGeometry.OpenOrdinaryDoors(npc.transform.position - view.Offset, ground);
                    npc.transform.position = ground + view.Offset;
                }
                npc.gameObject.SetActive(view.WasActive && witness.BuildingKey == interior.EntryDoor.buildingKey);
            }
        }
        private static void RestoreKeeperView(KeeperView view)
        { if (view.Npc) { view.Npc.transform.position = view.Position; view.Npc.gameObject.SetActive(view.WasActive); } }
        private void RestoreKeeperViews()
        {
            if (interior) foreach (var pose in interior.GetComponentsInChildren<SleepingPose>(true))
                if (pose.GetComponent<StaticNPC>()) pose.Wake();
            foreach (var view in keeperViews.Values) RestoreKeeperView(view);
            keeperViews.Clear(); keeperSchedules.Clear();
        }
        private float nextKeeperSurvey;
        private DaggerfallInterior keeperRoom;
        private HomeProfile keeperHome;
        private List<HomeProfile> keeperHomes;
        private void SurveyKeeperIdentities()
        {
            if (!interior || worldStore == null || population == null || !directory) return;
            if (keeperRoom == interior && Time.unscaledTime < nextKeeperSurvey) return;
            if (keeperRoom != interior)
            {
                keeperRoom = interior; keeperHome = null; keeperHomes = null;
                BuildingSummary shop;
                if (directory.GetBuildingSummary(interior.EntryDoor.buildingKey, out shop) && RMBLayout.IsShop(shop.BuildingType))
                {
                    var dfu = DaggerfallUnity.Instance;
                    keeperHome = HousingSurvey.Read(dfu.ContentReader, directory.LocationData, new[] { shop }, dfu.MeshReader)[0];
                    keeperHomes = HousingSurvey.Read(directory);
                }
            }
            nextKeeperSurvey = Time.unscaledTime + 1;
            if (keeperHome == null) return;
            var keepers = new List<NativePerson>();
            foreach (var npc in interior.GetComponentsInChildren<StaticNPC>(true))
            {
                var data = npc.Data;
                if (data.context != StaticNPC.Context.Building || data.buildingKey != keeperHome.BuildingKey || npc.IsChildNPC ||
                    npc.GetComponent<QuestResourceBehaviour>() || npc.GetComponent<DaggerfallAction>()) continue;
                FactionFile.FactionData faction;
                if (!GameManager.Instance.PlayerEntity.FactionData.GetFactionData(data.factionID, out faction) ||
                    faction.type == (int)FactionFile.FactionTypes.Individual || faction.sgroup != (int)FactionFile.SocialGroups.Merchants) continue;
                var keeper = worldStore.RegisterNativePerson(population.SettlementId, keeperHome.BuildingKey, data.hash, npc.DisplayName,
                    data.gender == DaggerfallWorkshop.Game.Entity.Genders.Female ? ResidentSex.Female : ResidentSex.Male,
                    (int)data.race, data.factionID, data.billboardArchiveIndex, data.billboardRecordIndex);
                if (keeper.HomeKey < 0 && worldStore.AssignNativeBed(keeper.Id, keeperHome, keeperHomes))
                    Debug.Log("LIVING_CITIES_KEEPER_HOME: " + keeper.Id + "; " + keeper.Name + "; home=" + keeper.HomeKey + "; bed=" + keeper.BedSlot);
                keepers.Add(keeper);
            }
            // Preserve all original staff reservations before adding a family to spare beds.
            foreach (var keeper in keepers)
            {
                var family = worldStore.CreateKeeperHousehold(keeper.Id, keeperHome);
                if (family != null)
                {
                    uint previousSeed = DFRandom.Seed;
                    try
                    {
                        foreach (var member in family.Residents)
                        {
                            if (member.FullName == null)
                            {
                                DFRandom.Seed = member.AppearanceSeed;
                                member.FullName = DaggerfallUnity.Instance.NameHelper.FirstName(NameHelper.BankTypes.Breton,
                                    member.Sex == ResidentSex.Female ? DaggerfallWorkshop.Game.Entity.Genders.Female : DaggerfallWorkshop.Game.Entity.Genders.Male) + " " + family.Surname;
                            }
                            if (member.Id != keeper.Id && !plans.Exists(p => p.Resident.Id == member.Id))
                                AddPlan(member, new ResidentRoutine { ResidentId = member.Id }, family.SurnameSeed, wakeHour, sleepHour);
                        }
                    }
                    finally { DFRandom.Seed = previousSeed; }
                    // Creation can happen after initial room setup; reserve the native head's bed before any family sleep actor.
                    if (bedClaims.Count == 0)
                    {
                        indoorBeds.Clear();
                        foreach (var bed in BedGeometry.Read(interior)) indoorBeds.Add(bed.RecordIndex, bed);
                        foreach (var claim in HomeBeds.Assign(family, indoorBeds.Keys)) bedClaims.Add(claim.ResidentId, claim);
                    }
                }
            }
        }
    }
}
