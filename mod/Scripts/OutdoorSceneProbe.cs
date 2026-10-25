using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public static class OutdoorSceneProbe
    {
        public static IEnumerator Run(PeopleOfTheIliacBayMod adapter, StreetPopulation streets, GuardPopulation guards)
        {
            var game = GameManager.Instance;
            var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            var geometry = new TownGeometry(directory, game.StreamingWorld.GetCurrentCityNavigation(), false);
            var store = (WorldStateStore)typeof(PeopleOfTheIliacBayMod).GetField("store", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(adapter);
            var population = (SettlementPopulation)typeof(PeopleOfTheIliacBayMod).GetField("current", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(adapter);
            typeof(PeopleOfTheIliacBayMod).GetField("exteriorGuardsEnabled", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(adapter, true);
            streets.ReactionsEnabled = true; game.PlayerEntity.GodMode = true;
            guards.SetTown(directory, game.StreamingWorld.GetCurrentCityNavigation(), population, store);
            if (guards.Roster == null) throw new InvalidOperationException("No roster for outdoor probe.");
            var guard = guards.Roster.Guards.Find(g => g.IsAlive && g.Duty == GuardDuty.Posted);
            var target = guard.Post;
            var point = geometry.ScenePoint(new RoutePoint(target.X, target.Z));
            game.PlayerEntity.CrimeCommitted = PlayerEntity.Crimes.None;
            game.PlayerMotor.transform.position = point + Vector3.right * 50 + Vector3.up;
            Resident victim = null;
            foreach (var home in population.Households)
                foreach (var person in home.Residents) if (person.IsAlive && person.AgeGroup != ResidentAgeGroup.Child) { victim = person; break; }
            double now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            var alarm = store.RecordViolence(population.SettlementId, victim.Id, now);
            store.LocateViolence(alarm.Id, -1, target.X, 0, target.Z);
            victim.IsAlive = false; store.ConfirmCorpse(alarm.Id, now, -1, target.X, directory.transform.InverseTransformPoint(point).y, target.Z);
            float deadline = Time.realtimeSinceStartup + 15;
            GuardReport report = null;
            while (Time.realtimeSinceStartup < deadline)
            {
                report = guards.Roster.Reports.Find(r => r.Id == "outdoor:" + alarm.Id && r.GuardIds.Count > 0);
                if (alarm.Corpse.Discovery != null && alarm.Corpse.Cleared && report != null) break;
                yield return null;
            }
            if (alarm.Corpse.Discovery == null || !alarm.Corpse.Cleared || report == null)
                throw new InvalidOperationException("Outdoor sight, departure cleanup or finite dispatch missing.");
            if (alarm.Corpse.Discovery.PlayerPresent || game.PlayerEntity.CrimeCommitted != PlayerEntity.Crimes.None)
                throw new InvalidOperationException("Absent player was accused.");
            var responder = guards.Roster.Guards.Find(g => g.Id == report.GuardIds[0]);
            DaggerfallUnity.Instance.WorldTime.Now.RaiseTime((float)Math.Max(0, responder.ArrivesAt - DaggerfallUnity.Instance.WorldTime.Now.ToSeconds() + 2));
            game.PlayerMotor.transform.position = point + Vector3.up;
            yield return new WaitForSecondsRealtime(1.5f);
            if (game.PlayerEntity.CrimeCommitted != PlayerEntity.Crimes.None)
                throw new InvalidOperationException("Later passerby was accused of murder.");
            var a = guards.Roster.Position(responder.Id, responder.ArrivesAt + 10);
            var b = guards.Roster.Position(responder.Id, responder.ArrivesAt + 30);
            if (Math.Abs(a.X-b.X) + Math.Abs(a.Z-b.Z) < 0.1 || report.SearchRoute == null)
                throw new InvalidOperationException("Investigator did not patrol the area.");
            string json = DaggerfallWorkshop.Game.Serialization.SaveLoadManager.Serialize(typeof(WorldState), adapter.GetSaveData());
            var restored = new WorldStateStore(0,PeopleOfTheIliacBayMod.ConversationArchiveRoot());
            restored.Restore((WorldState)DaggerfallWorkshop.Game.Serialization.SaveLoadManager.Deserialize(typeof(WorldState), json));
            if (!restored.OutdoorCorpses(population.SettlementId)[0].Corpse.Cleared)
                throw new InvalidOperationException("Outdoor cleanup lost on native save round trip.");
            var saved = restored.GetGuards(population.SettlementId);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PATROL_ROUNDTRIP: before=" + responder.SearchUntil.ToString("R") + "; after=" + saved.Guards.Find(g => g.Id == responder.Id).SearchUntil.ToString("R"));
            // Native JSON rounds large absolute timestamps by a few microseconds.
            if (Math.Abs(saved.Guards.Find(g => g.Id == responder.Id).SearchUntil - responder.SearchUntil) > 0.001)
                throw new InvalidOperationException("Patrol deadline changed on reload.");
            Resident other = null;
            foreach (var home in population.Households)
                foreach (var person in home.Residents) if (person.IsAlive && person.AgeGroup != ResidentAgeGroup.Child) { other = person; break; }
            now = DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            var examined = store.RecordViolence(population.SettlementId, other.Id, now);
            store.LocateViolence(examined.Id, -1, target.X, 0, target.Z);
            other.IsAlive = false;
            store.ConfirmCorpse(examined.Id, now, -1, target.X, directory.transform.InverseTransformPoint(point).y, target.Z);
            yield return new WaitForSecondsRealtime(1);
            CorpseInteraction action = null;
            foreach (var item in UnityEngine.Object.FindObjectsOfType<CorpseInteraction>())
                if (item.IncidentId == examined.Id) { action = item; break; }
            if (!action) throw new InvalidOperationException("On-site corpse view missing.");
            action.Activate(new RaycastHit { distance = 1 });
            if (game.PlayerEntity.CrimeCommitted != PlayerEntity.Crimes.None)
                throw new InvalidOperationException("Examining a corpse or presence alone caused a crime.");
            var loot = DaggerfallUI.Instance.InventoryWindow.LootTarget;
            if (!loot || loot.Items.Count != 1 || !HouseholdPropertyKeyItem.IsMatching(loot.Items.GetItem(0),population.SettlementId,other.HomeKey))
                throw new InvalidOperationException("Corpse inventory did not expose its owner's home key.");
            var takenKey = loot.Items.GetItem(0);
            game.PlayerEntity.Items.TransferAll(loot.Items);
            if (!game.PlayerEntity.Items.Contains(takenKey) || store.GetAvailableCorpsePropertyKeyClaims(examined.Id).Count != 0)
                throw new InvalidOperationException("Native take-all did not persist the corpse key claim.");
            var keyReload = new WorldStateStore(0,PeopleOfTheIliacBayMod.ConversationArchiveRoot()); keyReload.Restore(store.Snapshot());
            if (keyReload.GetAvailableCorpsePropertyKeyClaims(examined.Id).Count != 0)
                throw new InvalidOperationException("Corpse key returned after world reload.");
            // Returning a taken key is a player deposit, not a replenished claim.
            loot.Items.Transfer(takenKey,game.PlayerEntity.Items);
            DaggerfallUI.Instance.PopToHUD();
            yield return null;
            if (!action) throw new InvalidOperationException("Empty inventory removed the corpse visual.");
            bool depositPreserved=false;
            foreach (var pile in UnityEngine.Object.FindObjectsOfType<DaggerfallLoot>())
                if (pile.ContainerType==LootContainerTypes.DroppedLoot && pile.Items.Contains(takenKey)) depositPreserved=true;
            if (!depositPreserved || store.GetAvailableCorpsePropertyKeyClaims(examined.Id).Count!=0)
                throw new InvalidOperationException("Returned corpse key was lost or recreated as an unclaimed key.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CORPSE_KEY_INVENTORY_PASS: native window, owner key, take-all, persistent depletion, deposit preserved, body retained.");
            var guardView = UnityEngine.Object.FindObjectOfType<GuardActor>();
            if (!guardView) throw new InvalidOperationException("No physical investigator view.");
            var activation = game.PlayerObject.GetComponent<PlayerActivate>();
            typeof(PlayerActivate).GetMethod("ActivateMobileEnemy", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(activation,
                new object[] { new RaycastHit { distance = 1 }, guardView.GetComponent<DaggerfallEntityBehaviour>() });
            if (DaggerfallUI.UIManager.TopWindow is DaggerfallWorkshop.Game.UserInterface.DaggerfallTalkWindow)
                throw new InvalidOperationException("Investigator opened conversation.");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_OUTDOOR_SCENE_PASS: physical sight, absent-player cleanup, finite patrol, later innocent arrival and native persistence.");
        }
    }
}
