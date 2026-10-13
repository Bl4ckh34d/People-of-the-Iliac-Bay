using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Serialization;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public static class CivilianResponseProbe
    {
        public static IEnumerator Run()
        {
            var game = GameManager.Instance;
            var adapter = UnityEngine.Object.FindObjectOfType<LivingCitiesMod>();
            var streets = UnityEngine.Object.FindObjectOfType<StreetPopulation>();
            if (!game.PlayerEnterExit.IsPlayerInsideBuilding) throw new InvalidOperationException("Response probe needs family interior.");
            streets.ReactionsEnabled = true;
            var all = UnityEngine.Object.FindObjectsOfType<ResidentActor>();
            ResidentActor victim = null, witness = null;
            foreach (var a in all) foreach (var b in all)
                if (a != b && a.Resident.IsAlive && b.Resident.IsAlive && b.Resident.AgeGroup == ResidentAgeGroup.Adult &&
                    CivilianResponses.Sees(a.transform.position, b.transform.position, 20)) { victim = a; witness = b; break; }
            if (!victim || !witness) throw new InvalidOperationException("Family response fixture needs a visible adult witness.");
            string victimId = victim.Resident.Id, witnessId = witness.Resident.Id;
            var witnessPerson = witness.Person; string name = witnessPerson.NameNPC;
            // Use the real native entry point patched by this bundle; preserve vanilla effects.
            game.WeaponManager.WeaponDamage(null, false, false, victim.transform, victim.transform.position, Vector3.forward);
            yield return null;
            var snapshot = (WorldState)adapter.GetSaveData();
            var alarm = snapshot.AlarmIncidents.Find(a => a.VictimId == victimId);
            if (alarm == null || !alarm.People.Exists(p => p.ResidentId == witnessId && p.DirectWitness) || victim.Resident.IsAlive)
                throw new InvalidOperationException("Native killing did not create the witnessed incident/death.");
            var store = (WorldStateStore)typeof(LivingCitiesMod).GetField("store", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(adapter);
            var live = store.ActiveAlarm(witnessId, DaggerfallUnity.Instance.WorldTime.Now.ToSeconds()).People.Find(p => p.ResidentId == witnessId);
            // Force both branches on this disposable test witness; stable default selection has core tests.
            live.Reaction = CivilianReaction.Flee;
            var origin = witness.transform.parent;
            Vector3 before = witness.transform.position;
            game.PlayerMotor.transform.position = before + witness.transform.forward * 1.4f;
            yield return new WaitForSecondsRealtime(1);
            witness = streets.FindActor(witnessId);
            if (!witness || witness.Person != witnessPerson || witness.Person.NameNPC != name ||
                Vector3.Distance(before, witness.transform.position) < 0.1f)
                throw new InvalidOperationException("Civilian failed to retreat while retaining identity.");
            live.Reaction = CivilianReaction.Fight;
            // Put the test player in clear melee range of the same actual resident.
            var anchors = new InteriorGeometry(game.PlayerEnterExit.Interior).LocalAnchors;
            bool positioned = false;
            foreach (var anchor in anchors)
            {
                foreach (var adjacent in anchors)
                {
                    var npcPoint = origin.TransformPoint(anchor) + Vector3.up;
                    var playerPoint = origin.TransformPoint(adjacent) + Vector3.up;
                    float distance = Vector3.Distance(npcPoint, playerPoint);
                    if (distance < 1.1f || distance > 1.6f || !CivilianResponses.Sees(npcPoint, playerPoint, 2)) continue;
                    live.X = anchor.x; live.Y = anchor.y; live.Z = anchor.z;
                    witness.Place(npcPoint - Vector3.up, playerPoint - npcPoint, true);
                    game.PlayerMotor.transform.position = playerPoint; positioned = true; break;
                }
                if (positioned) break;
            }
            if (!positioned) throw new InvalidOperationException("No verified melee fixture floor pair.");
            int strikes = witness.Counterattacks;
            // Keep the controlled target fixed: native guard knockback/gravity is a different system.
            bool motorEnabled = game.PlayerMotor.enabled;
            game.PlayerMotor.enabled = false;
            try { yield return new WaitForSecondsRealtime(2); }
            finally { game.PlayerMotor.enabled = motorEnabled; }
            if (witness.Counterattacks <= strikes || witness.Person != witnessPerson)
                throw new InvalidOperationException("Civilian did not counterattack: distance=" + Vector3.Distance(witness.transform.position, game.PlayerMotor.transform.position) +
                    "; paused=" + GameManager.IsGamePaused + "; LOS=" + CivilianResponses.Sees(witness.transform.position, game.PlayerMotor.transform.position, 2));
            game.TalkManager.TalkToMobileNPC(witness.Person);
            DaggerfallUI.Instance.PopToHUD();
            DaggerfallUI.UIManager.PushWindow(DaggerfallUI.Instance.TalkWindow);
            yield return null;
            if (DaggerfallUI.UIManager.TopWindow == DaggerfallUI.Instance.TalkWindow)
                throw new InvalidOperationException("Alarmed resident still permits ordinary conversation.");
            DaggerfallUI.Instance.PopToHUD();
            // Native serializer and real save/load must retain reaction and spent warning data.
            bool saved = false, loaded = false;
            SaveLoadManager.OnSaveEventHandler onSave = ignored => saved = true;
            SaveLoadManager.OnLoadEventHandler onLoad = ignored => loaded = true;
            SaveLoadManager.OnSave += onSave; SaveLoadManager.OnLoad += onLoad;
            try
            {
                game.SaveLoadManager.Save(game.PlayerEntity.Name, "Living Cities - civilian response");
                float deadline = Time.realtimeSinceStartup + 30;
                while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
                if (!saved) throw new InvalidOperationException("Response save timed out.");
                int key = game.SaveLoadManager.FindSaveFolderByNames(game.PlayerEntity.Name, "Living Cities - civilian response");
                game.SaveLoadManager.Load(key); DaggerfallUI.Instance.PopToHUD();
                deadline = Time.realtimeSinceStartup + 60;
                while ((!loaded || !streets.Ready) && Time.realtimeSinceStartup < deadline) yield return null;
                if (!loaded || !streets.Ready) throw new InvalidOperationException("Response reload timed out.");
                yield return new WaitForSecondsRealtime(0.5f);
                var restored = ((WorldState)adapter.GetSaveData()).AlarmIncidents.Find(a => a.Id == alarm.Id);
                witness = streets.FindActor(witnessId);
                if (restored == null || !restored.People.Exists(p => p.ResidentId == witnessId && p.Reaction == CivilianReaction.Fight) ||
                    !witness || witness.Person.NameNPC != name)
                    throw new InvalidOperationException("Incident or reacting identity missing after reload.");
                Debug.Log("LIVING_CITIES_TOWN_RESPONSE_PASS: native weapon hook, witnessed murder, retreat, counterattack, talk refusal, identity and real save/load; victim=" + victimId + "; witness=" + witnessId + "; save=" + key);
            }
            finally { SaveLoadManager.OnSave -= onSave; SaveLoadManager.OnLoad -= onLoad; }
        }
    }
}
