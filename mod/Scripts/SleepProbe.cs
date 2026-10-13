using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Serialization;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public static class SleepProbe
    {
        public static IEnumerator Run()
        {
            var game = GameManager.Instance;
            var adapter = UnityEngine.Object.FindObjectOfType<LivingCitiesMod>();
            var streets = UnityEngine.Object.FindObjectOfType<StreetPopulation>();
            streets.SleepEnabled = true;
            if (game.PlayerEnterExit.IsPlayerInside) game.PlayerEnterExit.TransitionExterior();
            yield return new WaitForSecondsRealtime(1);
            var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            var town = ((WorldState)adapter.GetSaveData()).Settlements.Find(t => t.SettlementId == directory.MapID);
            Resident selected = null; DaggerfallStaticDoors owner = null; StaticDoor door = default(StaticDoor);
            foreach (var home in town.Households)
            {
                if (home.Residents.Count != 1) continue;
                var resident = home.Residents[0];
                if (!resident.IsAlive || resident.AgeGroup != ResidentAgeGroup.Adult || resident.Sex != ResidentSex.Male ||
                    ((resident.AppearanceSeed >> 1) % 4) != 0) continue;
                foreach (var candidate in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                    foreach (var entry in candidate.Doors)
                    {
                        if (entry.doorType != DoorTypes.Building || entry.buildingKey != home.BuildingKey) continue;
                        var block = DaggerfallUnity.Instance.ContentReader.BlockFileReader.GetBlock(entry.blockIndex);
                        foreach (var record in block.RmbBlock.SubRecords[entry.recordIndex].Interior.Block3dObjectRecords)
                            if (record.ModelIdNum >= 41000 && record.ModelIdNum <= 41002)
                            { selected = resident; owner = candidate; door = entry; break; }
                    }
                if (selected != null) break;
            }
            if (selected == null) throw new InvalidOperationException("No matching sleep-art resident fixture.");
            SetHour(23);
            TownSmokeHarness.EnterInterior(owner.transform, door);
            yield return new WaitForSecondsRealtime(1); DaggerfallUI.Instance.PopToHUD();
            yield return new WaitForSecondsRealtime(1);
            var actor = streets.FindActor(selected.Id);
            if (!actor || !actor.IsSleeping) throw new InvalidOperationException("Scheduled resident did not enter bed pose.");
            string name = actor.Person.NameNPC;
            var person = actor.Person; var body = actor.GetComponent<CapsuleCollider>();
            var bed = BedGeometry.Read(game.PlayerEnterExit.Interior)[0];
            Vector3 mattress = game.PlayerEnterExit.Interior.transform.TransformPoint(bed.LocalCenter);
            if (Vector3.Distance(actor.transform.position, mattress) > 0.4f || body.direction != 2 || !body.enabled)
                throw new InvalidOperationException("Sleeping body is not anchored/interactable at the mattress.");
            yield return FrameSleeper(actor);
            SetHour(0); yield return null;
            if (!actor.IsSleeping || actor.Person != person) throw new InvalidOperationException("Midnight changed sleeping identity.");
            SetHour(7); yield return null;
            if (actor.IsSleeping || actor.Person != person || actor.Person.NameNPC != name || body.direction != 1 ||
                !actor.Person.Asset.gameObject.activeSelf)
                throw new InvalidOperationException("Morning did not restore original person and upright body.");
            yield return new WaitForSecondsRealtime(0.2f);
            AimCamera(actor.transform.position);
            yield return null;
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.persistentDataPath, "LivingCities-waking.png"));
            SetHour(23); yield return null;
            if (!actor.IsSleeping) throw new InvalidOperationException("Next bedtime did not restore pose.");
            streets.SleepEnabled = false; yield return null;
            if (actor.IsSleeping || body.direction != 1) throw new InvalidOperationException("Sleep toggle did not restore standing actor.");
            streets.SleepEnabled = true; yield return null;
            if (!actor.IsSleeping) throw new InvalidOperationException("Sleep toggle did not resume scheduled sleep.");
            yield return FrameSleeper(actor);
            bool saved = false, loaded = false;
            SaveLoadManager.OnSaveEventHandler onSave = ignored => saved = true;
            SaveLoadManager.OnLoadEventHandler onLoad = ignored => loaded = true;
            SaveLoadManager.OnSave += onSave; SaveLoadManager.OnLoad += onLoad;
            try
            {
                game.SaveLoadManager.Save(game.PlayerEntity.Name, "Living Cities - sleeping preview");
                float deadline = Time.realtimeSinceStartup + 30;
                while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
                if (!saved) throw new InvalidOperationException("Sleep preview save timed out.");
                yield return null;
                int key = game.SaveLoadManager.FindSaveFolderByNames(game.PlayerEntity.Name, "Living Cities - sleeping preview");
                game.SaveLoadManager.Load(key); DaggerfallUI.Instance.PopToHUD();
                deadline = Time.realtimeSinceStartup + 45;
                while ((!loaded || !streets.Ready) && Time.realtimeSinceStartup < deadline) yield return null;
                yield return new WaitForSecondsRealtime(1);
                var restored = streets.FindActor(selected.Id);
                if (!loaded || !restored || !restored.IsSleeping || restored.Person.NameNPC != name)
                    throw new InvalidOperationException("Sleeping identity/pose lost after actual reload.");
                yield return FrameSleeper(restored);
                restored.GetComponent<DaggerfallWorkshop.Game.Entity.DaggerfallEntityBehaviour>().Entity.SetHealth(0);
                yield return null;
                if (restored.Resident.IsAlive || streets.FindActor(selected.Id))
                    throw new InvalidOperationException("Sleeping actor death did not remove the resident.");
                saved = false;
                game.SaveLoadManager.Save(game.PlayerEntity.Name, "Living Cities - sleeping death");
                deadline = Time.realtimeSinceStartup + 30;
                while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
                if (!saved) throw new InvalidOperationException("Sleeping death save timed out.");
                yield return null;
                loaded = false;
                game.SaveLoadManager.Load(game.SaveLoadManager.FindSaveFolderByNames(game.PlayerEntity.Name, "Living Cities - sleeping death"));
                DaggerfallUI.Instance.PopToHUD(); deadline = Time.realtimeSinceStartup + 45;
                while ((!loaded || !streets.Ready) && Time.realtimeSinceStartup < deadline) yield return null;
                yield return new WaitForSecondsRealtime(1);
                var deadHome = ((WorldState)adapter.GetSaveData()).Settlements.Find(t => t.SettlementId == town.SettlementId)
                    .Households.Find(h => h.BuildingKey == selected.HomeKey);
                if (!loaded || deadHome.Residents.Find(r => r.Id == selected.Id).IsAlive || streets.FindActor(selected.Id))
                    throw new InvalidOperationException("Sleeping death did not persist after reload.");
                loaded = false; game.SaveLoadManager.Load(key); DaggerfallUI.Instance.PopToHUD();
                deadline = Time.realtimeSinceStartup + 45;
                while ((!loaded || !streets.Ready) && Time.realtimeSinceStartup < deadline) yield return null;
                yield return new WaitForSecondsRealtime(1);
                if (!loaded || !streets.FindActor(selected.Id) || !streets.FindActor(selected.Id).IsSleeping)
                    throw new InvalidOperationException("Could not restore living preview after death check.");
            }
            finally { SaveLoadManager.OnSave -= onSave; SaveLoadManager.OnLoad -= onLoad; }
            Debug.Log("LIVING_CITIES_TOWN_SLEEP_PASS: resident=" + selected.Id + "; name=" + name +
                "; building=" + selected.HomeKey + "; bedtime, midnight, waking, toggle, raycast target, death and actual save/load");
        }
        private static IEnumerator FrameSleeper(ResidentActor actor)
        {
            var game = GameManager.Instance;
            Vector3 target = actor.transform.position + Vector3.up * 0.1f;
            bool found = false;
            var observedViews = new HashSet<int>();
            Vector3 sidePosition = Vector3.zero;
            var interior = game.PlayerEnterExit.Interior;
            foreach (var direction in new[] { actor.transform.right, -actor.transform.right, -actor.transform.forward, actor.transform.forward })
            {
                Vector3 desired = target + direction * 2 + Vector3.up * 1.1f;
                Vector3 floor = Vector3.zero; float nearestFloor = float.MaxValue;
                // Standing anchors intentionally do not traverse stairs. This bed
                // is upstairs, so find its local floor rather than downstairs anchors.
                foreach (var ground in Physics.RaycastAll(desired, Vector3.down, 3, ~0, QueryTriggerInteraction.Ignore))
                    if (ground.normal.y >= 0.9f && ground.collider.transform.IsChildOf(interior.transform) && ground.distance < nearestFloor)
                    { floor = ground.point; nearestFloor = ground.distance; }
                if (nearestFloor == float.MaxValue) continue;
                RaycastHit obstruction;
                if (Physics.Linecast(desired, target, out obstruction, ~0, QueryTriggerInteraction.Ignore))
                { Debug.Log("LIVING_CITIES_SLEEP_CAMERA_BLOCKED: desired=" + desired + "; target=" + target + "; hit=" + obstruction.collider.name); continue; }
                game.PlayerMotor.CancelMovement = true;
                game.PlayerMotor.transform.position = floor + Vector3.up;
                yield return new WaitForSecondsRealtime(0.2f);
                AimCamera(target);
                yield return null;
                yield return new WaitForEndOfFrame();
                Vector3 view = Camera.main.WorldToViewportPoint(target);
                Debug.Log("LIVING_CITIES_SLEEP_CAMERA: desired=" + desired + "; actual=" + Camera.main.transform.position +
                    "; viewport=" + view + "; pitch=" + game.PlayerMouseLook.Pitch + "; rotation=" + Camera.main.transform.eulerAngles);
                if (view.z <= 0 || Mathf.Abs(view.x - 0.5f) > 0.05f || Mathf.Abs(view.y - 0.5f) > 0.05f ||
                    Physics.Linecast(Camera.main.transform.position, target, ~0, QueryTriggerInteraction.Ignore)) continue;
                bool targetable = false;
                foreach (var hit in Physics.RaycastAll(Camera.main.transform.position,
                    actor.transform.position - Camera.main.transform.position, 5, ~0, QueryTriggerInteraction.Collide))
                    if (hit.collider.GetComponentInParent<ResidentActor>() == actor) targetable = true;
                if (!targetable) throw new InvalidOperationException("Sleeping actor has no raycast interaction target.");
                int index = actor.GetComponent<SleepingPose>().ViewIndex;
                observedViews.Add(index);
                if (index == 2) { sidePosition = game.PlayerMotor.transform.position; found = true; }
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.persistentDataPath,
                    "LivingCities-sleeping-view-" + index + ".png"));
                yield return null;
            }
            if (!found || !observedViews.Contains(0)) throw new InvalidOperationException("Sleeping fixture needs visible side and foot-end views.");
            game.PlayerMotor.CancelMovement = true;
            game.PlayerMotor.transform.position = sidePosition;
            yield return new WaitForSecondsRealtime(0.5f);
            AimCamera(target);
            yield return null; yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.persistentDataPath, "LivingCities-sleeping.png"));
            Debug.Log("LIVING_CITIES_TOWN_SLEEP_VIEW: view=" + actor.GetComponent<SleepingPose>().ViewIndex +
                "; distinctViews=" + observedViews.Count + "; camera=" + Camera.main.transform.position + "; sleeper=" + actor.transform.position);
            yield return new WaitForEndOfFrame();
        }
        internal static void AimCamera(Vector3 target)
        {
            var look = GameManager.Instance.PlayerMouseLook;
            look.SetFacing(target - Camera.main.transform.position);
            // SetFacing only changes fields. Native Update can return early when
            // cursor/focus state suppresses looking in this unattended test process.
            // Apply its documented body-yaw/camera-pitch transform split explicitly.
            if (look.characterBody)
            {
                look.characterBody.transform.localEulerAngles = new Vector3(0, look.Yaw, 0);
                look.transform.localEulerAngles = new Vector3(look.Pitch, 0, 0);
            }
            else look.transform.localEulerAngles = new Vector3(look.Pitch, look.Yaw, 0);
        }
        private static void SetHour(int hour)
        {
            var now = DaggerfallUnity.Instance.WorldTime.Now;
            now.RaiseTime((86400 + hour * 3600 - (int)(now.ToSeconds() % 86400)) % 86400);
        }
    }
}
