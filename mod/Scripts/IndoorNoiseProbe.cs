using System;
using System.Collections;
using System.Reflection;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public static class IndoorNoiseProbe
    {
        public static IEnumerator Run(string keeperId)
        {
            var game = GameManager.Instance;
            var adapter = UnityEngine.Object.FindObjectOfType<PeopleOfTheIliacBayMod>();
            var streets = adapter.GetComponent<StreetPopulation>();
            var store = (WorldStateStore)typeof(PeopleOfTheIliacBayMod).GetField("store", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(adapter);
            var keeper = store.Snapshot().NativePeople.Find(p => p.Id == keeperId);
            var room = game.PlayerEnterExit.Interior;
            streets.ReactionsEnabled = true; streets.SleepEnabled = true;
            game.PlayerEntity.CrimeCommitted = DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.None;
            var clock = DaggerfallUnity.Instance.WorldTime.Now;
            clock.RaiseTime((float)((84600-clock.ToSeconds()%86400+86400)%86400));
            yield return new WaitForSecondsRealtime(1);
            var npc = Array.Find(room.GetComponentsInChildren<StaticNPC>(true), n=>n.Data.hash == keeper.NativeHash);
            var pose = npc.GetComponent<SleepingPose>();
            if (!pose || !pose.Active) throw new InvalidOperationException("Indoor noise fixture keeper is not sleeping.");
            Vector3 bedPosition = npc.transform.position;
            var town = store.Snapshot().Settlements.Find(t=>t.SettlementId == keeper.SettlementId);
            Resident victim = null;
            foreach (var home in town.Households) foreach (var person in home.Residents)
                if (victim == null && person.IsAlive && !person.IsQuestProtected && person.Id != keeperId) victim = person;
            var source = npc.transform.position + room.transform.right * 3;
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "Controlled indoor noise partition";
            wall.transform.SetParent(room.transform, true);
            wall.transform.position = (source+npc.transform.position)*0.5f;
            wall.transform.rotation = room.transform.rotation;
            wall.transform.localScale = new Vector3(0.2f,3,3);
            yield return null;
            var randomState = UnityEngine.Random.state;
            try
            {
                if (CivilianResponses.Sees(npc.transform.position, source, 20))
                    throw new InvalidOperationException("Noise fixture partition does not block sight.");
                double now = clock.ToSeconds();
                var alarm = store.RecordViolence(keeper.SettlementId, victim.Id, now);
                var local = room.transform.InverseTransformPoint(source);
                store.LocateViolence(alarm.Id, keeper.WorkKey, local.x, local.y, local.z);
                // Controlled successful roll tests the live wake path; random
                // probability extremes and failed-roll persistence have core tests.
                int seed = 0;
                for (; seed < 10000; seed++)
                {
                    UnityEngine.Random.InitState(seed);
                    if (UnityEngine.Random.Range(0,100) < 50) break;
                }
                UnityEngine.Random.InitState(seed);
                streets.ObserveKeeperViolence(alarm, source, now);
                var observer = alarm.People.Find(p=>p.ResidentId == keeperId);
                if (observer == null || !observer.HeardViolence || !observer.Awakened || observer.DirectWitness)
                    throw new InvalidOperationException("Sleeping keeper did not hear blocked indoor violence as unidentified noise.");
                yield return new WaitForSecondsRealtime(0.25f);
                if (pose.Active || !streets.KeeperServiceBlocked(npc))
                    throw new InvalidOperationException("Heard violence did not wake native keeper and block services.");
                if (Vector3.Distance(npc.transform.position,bedPosition)>3 || Mathf.Abs(npc.transform.position.y-bedPosition.y)>1.5f)
                    throw new InvalidOperationException("Awakened keeper teleported away from its bed instead of standing beside it.");
                if (observer.Escape==null)
                    throw new InvalidOperationException("Awakened keeper did not prepare an automatic bed-origin escape.");
                if (game.PlayerEntity.CrimeCommitted != DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.None)
                    throw new InvalidOperationException("Hearing alone accused player of murder.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INDOOR_NOISE_PASS: controlled blocked sound wakes real native keeper, retains identity, blocks service and does not identify player");
                bool saved = false, loaded = false;
                SaveLoadManager.OnSaveEventHandler onSave = ignored => saved = true;
                SaveLoadManager.OnLoadEventHandler onLoad = ignored => loaded = true;
                SaveLoadManager.OnSave += onSave; SaveLoadManager.OnLoad += onLoad;
                try
                {
                    const string name = "People of the Iliac Bay - indoor noise";
                    game.SaveLoadManager.Save(game.PlayerEntity.Name, name);
                    float deadline = Time.realtimeSinceStartup + 45;
                    while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
                    if (!saved) throw new InvalidOperationException("Noise wake save timed out.");
                    int slot = game.SaveLoadManager.FindSaveFolderByNames(game.PlayerEntity.Name, name);
                    game.SaveLoadManager.Load(slot); DaggerfallUI.Instance.PopToHUD();
                    deadline = Time.realtimeSinceStartup + 45;
                    while ((!loaded || !streets.Ready) && Time.realtimeSinceStartup < deadline) yield return null;
                    if (!loaded || !streets.Ready) throw new InvalidOperationException("Noise wake reload timed out.");
                    yield return new WaitForSecondsRealtime(0.25f);
                    clock = DaggerfallUnity.Instance.WorldTime.Now;
                    store = (WorldStateStore)typeof(PeopleOfTheIliacBayMod).GetField("store", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(adapter);
                    room = game.PlayerEnterExit.Interior;
                    npc = Array.Find(room.GetComponentsInChildren<StaticNPC>(true), n=>n.Data.hash == keeper.NativeHash);
                    pose = npc.GetComponent<SleepingPose>();
                    alarm = store.ActiveAlarm(keeperId, clock.ToSeconds());
                    observer = alarm == null ? null : alarm.People.Find(p=>p.ResidentId == keeperId);
                    if (observer == null || !observer.Awakened || !observer.HeardViolence || observer.DirectWitness ||
                        (pose && pose.Active) || !streets.KeeperServiceBlocked(npc))
                        throw new InvalidOperationException("Actual reload lost native waking/hearing state.");
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INDOOR_NOISE_RELOAD_PASS: actual save/load preserves woken keeper and unidentified evidence; save="+slot);
                }
                finally { SaveLoadManager.OnSave -= onSave; SaveLoadManager.OnLoad -= onLoad; }
                // Continue the actual automatically planned bed-origin journey.
                var guards = adapter.GetComponent<GuardPopulation>();
                if (observer.Escape==null) throw new InvalidOperationException("Reload lost automatic bed-origin journey.");
                var liveTown = store.GetOrCreate(keeper.SettlementId, new int[0]);
                Population.MarkDead(liveTown, victim.Id); store.ConfirmCorpse(alarm.Id, clock.ToSeconds());
                store.RegisterUnwitnessedScene(alarm.Id);
                if (guards.Roster.Reports.Exists(r=>r.Id == "discovery:"+alarm.Id))
                    throw new InvalidOperationException("Noise created guards before physical report.");
                clock.RaiseTime((float)Math.Max(0, observer.Escape.ReportAt-clock.ToSeconds()+1));
                float reportDeadline = Time.realtimeSinceStartup + 3;
                while (!alarm.Scene.Reported && Time.realtimeSinceStartup < reportDeadline) yield return null;
                var report = guards.Roster.Reports.Find(r=>r.Id == "discovery:"+alarm.Id);
                if (!alarm.Scene.Reported || report == null || !report.Investigation || report.GuardIds.Count == 0 ||
                    game.PlayerEntity.CrimeCommitted != DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.None)
                    throw new InvalidOperationException("Physical hearer report did not dispatch neutral finite guards.");
                double arrival = double.MaxValue;
                foreach (var guard in guards.Roster.Guards) if (guard.IncidentId == report.Id) arrival = Math.Min(arrival, guard.ArrivesAt);
                clock.RaiseTime((float)Math.Max(0, arrival-clock.ToSeconds()+1));
                float arrivalDeadline = Time.realtimeSinceStartup + 3;
                while (!alarm.Scene.Discovered && Time.realtimeSinceStartup < arrivalDeadline) yield return null;
                if (!alarm.Scene.Discovered || !report.HasArrival || game.PlayerEntity.CrimeCommitted != DaggerfallWorkshop.Game.Entity.PlayerEntity.Crimes.None)
                    throw new InvalidOperationException("Neutral guards failed to arrive without murder accusation.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_INDOOR_NOISE_REPORT_PASS: actual automatic bed-origin escape, neutral dispatch, actual guard arrival and no murder accusation");
            }
            finally { UnityEngine.Random.state = randomState; UnityEngine.Object.Destroy(wall); }
        }
    }
}
