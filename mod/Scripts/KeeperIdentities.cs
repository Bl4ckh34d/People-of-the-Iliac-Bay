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
        private void InstallKeeperServiceHook()
        {
            var type = System.Type.GetType("LivingCities.QuestHooks.NativeServiceHooks, LivingCities.QuestHooks", true);
            keeperServiceHook = (System.IDisposable)System.Activator.CreateInstance(type,
                new object[] { new System.Func<StaticNPC, bool>(KeeperServiceBlocked) });
        }
        public bool KeeperServiceBlocked(StaticNPC npc)
        {
            var person = KeeperPerson(npc);
            return person != null && (!person.IsAlive || ReactionsEnabled &&
                worldStore.NativeResponse(person.Id) != null);
        }
        private NativePerson KeeperPerson(StaticNPC npc)
        {
            if (!npc || !interior || worldStore == null || population == null ||
                npc.GetComponent<QuestResourceBehaviour>() || npc.GetComponent<DaggerfallAction>() ||
                npc.Data.context != StaticNPC.Context.Building || npc.Data.buildingKey != interior.EntryDoor.buildingKey) return null;
            return worldStore.NativePeopleFor(population.SettlementId).Find(p => p.WorkKey == npc.Data.buildingKey && p.NativeHash == npc.Data.hash);
        }
        public void ObserveKeeperViolence(CivilianIncident alarm, Vector3 victim, double now)
        {
            if (!ReactionsEnabled || !interior || alarm == null) return;
            SurveyKeeperIdentities();
            foreach (var npc in interior.GetComponentsInChildren<StaticNPC>())
            {
                var person = KeeperPerson(npc);
                if (person == null || !person.IsAlive || !CivilianResponses.Sees(npc.transform.position, victim, 20)) continue;
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
        { foreach (var view in keeperViews.Values) RestoreKeeperView(view); keeperViews.Clear(); }
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
            foreach (var npc in interior.GetComponentsInChildren<StaticNPC>())
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
            }
        }
    }
}
