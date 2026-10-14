using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
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
            yield return RunAppearance(ResidentSex.Male);
            yield return RunAppearance(ResidentSex.Female);
        }
        private static IEnumerator RunAppearance(ResidentSex sex)
        {
            string suffix = sex == ResidentSex.Male ? "" : " - female";
            string previewName = "Living Cities - sleeping preview" + suffix;
            string deathName = "Living Cities - sleeping death" + suffix;
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
                if (!resident.IsAlive || resident.AgeGroup != ResidentAgeGroup.Adult || resident.Sex != sex ||
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
            if (selected == null) throw new InvalidOperationException("No matching sleep-art resident fixture: " + sex);
            SetHour(23);
            TownSmokeHarness.EnterInterior(owner.transform, door);
            yield return new WaitForSecondsRealtime(1); DaggerfallUI.Instance.PopToHUD();
            yield return new WaitForSecondsRealtime(1);
            var actor = streets.FindActor(selected.Id);
            if (!actor || !actor.IsSleeping) throw new InvalidOperationException("Scheduled resident did not enter bed pose: " + sex);
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
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.persistentDataPath, "LivingCities-waking" + suffix + ".png"));
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
                game.SaveLoadManager.Save(game.PlayerEntity.Name, previewName);
                float deadline = Time.realtimeSinceStartup + 30;
                while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
                if (!saved) throw new InvalidOperationException("Sleep preview save timed out.");
                yield return null;
                int key = game.SaveLoadManager.FindSaveFolderByNames(game.PlayerEntity.Name, previewName);
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
                game.SaveLoadManager.Save(game.PlayerEntity.Name, deathName);
                deadline = Time.realtimeSinceStartup + 30;
                while (!saved && Time.realtimeSinceStartup < deadline) yield return null;
                if (!saved) throw new InvalidOperationException("Sleeping death save timed out.");
                yield return null;
                loaded = false;
                game.SaveLoadManager.Load(game.SaveLoadManager.FindSaveFolderByNames(game.PlayerEntity.Name, deathName));
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
                actor=streets.FindActor(selected.Id); Vector3 bedOrigin=actor.transform.position;
                var store=(WorldStateStore)typeof(LivingCitiesMod).GetField("store",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(adapter);
                var wakeWorld=store.Snapshot();
                var live=wakeWorld.Settlements.Find(t=>t.SettlementId==town.SettlementId);
                Resident soundVictim=null;
                foreach (var home in live.Households) foreach (var resident in home.Residents)
                    if (soundVictim==null && resident.Id!=selected.Id && resident.IsAlive && !wakeWorld.AlarmIncidents.Exists(a=>a.VictimId==resident.Id)) soundVictim=resident;
                double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                var alarm=store.RecordViolence(town.SettlementId,soundVictim.Id,now);
                Vector3 local=game.PlayerEnterExit.Interior.transform.InverseTransformPoint(bedOrigin);
                store.LocateViolence(alarm.Id,selected.HomeKey,local.x,local.y,local.z);
                var response=store.HearViolence(alarm.Id,selected.Id,selected.HomeKey,now,true,0,3,0);
                response.HasPosition=true; response.BuildingKey=selected.HomeKey; response.X=local.x; response.Y=local.y; response.Z=local.z;
                streets.ReactionsEnabled=true;
                deadline=Time.realtimeSinceStartup+2;
                while (response.Escape==null && Time.realtimeSinceStartup<deadline) yield return null;
                if (actor.IsSleeping || response.Escape==null)
                    throw new InvalidOperationException("Generated sleeper did not stand beside bed and plan an automatic escape: " + sex);
                var start=response.Escape.InteriorRoute[0];
                Vector3 ground=game.PlayerEnterExit.Interior.transform.TransformPoint(new Vector3((float)start.X,(float)start.Y,(float)start.Z));
                var bedOffset=game.PlayerEnterExit.Interior.transform.InverseTransformPoint(ground)-bed.LocalCenter;
                if (Mathf.Abs(Vector3.Dot(bedOffset,bed.LocalSideDirection))<bed.Width*0.5f+0.15f &&
                    Mathf.Abs(Vector3.Dot(bedOffset,bed.LocalHeadDirection))<bed.Length*0.5f+0.15f)
                    throw new InvalidOperationException("Generated escape starts on the bed instead of beside it: "+sex);
                if (Vector3.Distance(ground,bedOrigin)>3 || ground.y>bedOrigin.y-0.1f || ground.y<bedOrigin.y-1.6f)
                    throw new InvalidOperationException("Generated sleeper escape did not start at bedside floor: " + sex);
                Debug.Log("LIVING_CITIES_GENERATED_BED_WAKE_PASS: sex="+sex+"; controlled noise; automatic bedside floor/escape; original identity retained.");
                double reportAt=response.Escape.ReportAt;
                saved=false;
                string wakingName="Living Cities - waking escape"+suffix;
                game.SaveLoadManager.Save(game.PlayerEntity.Name,wakingName);
                deadline=Time.realtimeSinceStartup+30;
                while (!saved && Time.realtimeSinceStartup<deadline) yield return null;
                if (!saved) throw new InvalidOperationException("Generated waking save timed out.");
                loaded=false;
                game.SaveLoadManager.Load(game.SaveLoadManager.FindSaveFolderByNames(game.PlayerEntity.Name,wakingName));
                DaggerfallUI.Instance.PopToHUD(); deadline=Time.realtimeSinceStartup+45;
                while ((!loaded || !streets.Ready) && Time.realtimeSinceStartup<deadline) yield return null;
                yield return new WaitForSecondsRealtime(0.25f);
                store=(WorldStateStore)typeof(LivingCitiesMod).GetField("store",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(adapter);
                alarm=store.ActiveAlarm(selected.Id,DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
                response=alarm==null ? null : alarm.People.Find(p=>p.ResidentId==selected.Id);
                actor=streets.FindActor(selected.Id);
                if (!loaded || response==null || !response.Awakened || response.Escape==null || Math.Abs(response.Escape.ReportAt-reportAt)>0.001 ||
                    (response.BuildingKey==selected.HomeKey && (!actor || actor.IsSleeping || actor.Person.NameNPC!=name)))
                    throw new InvalidOperationException("Generated waking/escape identity or timing lost after reload: "+sex);
                Debug.Log("LIVING_CITIES_GENERATED_BED_WAKE_RELOAD_PASS: sex="+sex+"; actual reload preserves awake identity and original automatic journey.");
            }
            finally { SaveLoadManager.OnSave -= onSave; SaveLoadManager.OnLoad -= onLoad; }
            Debug.Log("LIVING_CITIES_TOWN_SLEEP_PASS: sex=" + sex + "; resident=" + selected.Id + "; name=" + name +
                "; building=" + selected.HomeKey + "; bedtime, midnight, waking, toggle, raycast target, death and actual save/load");
        }
        private static IEnumerator FrameSleeper(ResidentActor actor)
        {
            var game = GameManager.Instance;
            string suffix = actor.Resident.Sex == ResidentSex.Male ? "" : "-female";
            Vector3 target = actor.transform.position + Vector3.up * 0.1f;
            bool found = false;
            var observedViews = new HashSet<int>();
            Vector3 sidePosition = Vector3.zero;
            Quaternion fixedRotation = Quaternion.identity;
            bool hasRotation = false;
            var interior = game.PlayerEnterExit.Interior;
            foreach (var direction in new[] { actor.transform.right, -actor.transform.right, -actor.transform.forward, actor.transform.forward })
            foreach (float distance in new[] { 2f, 1.2f })
            {
                Vector3 desired = target + direction * distance + Vector3.up * 1.1f;
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
                var blanket = actor.transform.Find("Sleeping blanket");
                var atlas = (SleepingArt.Atlas)typeof(SleepingPose).GetField("art", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(actor.GetComponent<SleepingPose>());
                float pixelAspect = atlas.Views[2].height*atlas.Texture.height/(atlas.Views[2].width*atlas.Texture.width);
                if (Mathf.Abs(blanket.localScale.x/blanket.localScale.y-pixelAspect)>0.001f)
                    throw new InvalidOperationException("Sleeping drawing stretches source pixels.");
                if (Mathf.Abs(Vector3.Dot(blanket.forward, Vector3.up)) < 0.999f ||
                    Vector3.Dot(blanket.up, actor.transform.forward) < 0.999f ||
                    (hasRotation && Quaternion.Angle(fixedRotation, blanket.rotation) > 0.01f))
                    throw new InvalidOperationException("Sleeper rotates away from mattress as player moves.");
                fixedRotation = blanket.rotation; hasRotation = true;
                Debug.Log("LIVING_CITIES_SLEEP_FOOTPRINT: sex=" + actor.Resident.Sex + "; view=" + index +
                    "; scale=" + blanket.localScale + "; centerOffset=" + (blanket.position - actor.transform.position));
                foreach (var vertex in blanket.GetComponent<MeshFilter>().sharedMesh.vertices)
                {
                    Vector3 corner = actor.transform.InverseTransformPoint(blanket.TransformPoint(vertex));
                    // A close camera may legitimately crop a real mattress. The
                    // relevant invariant is the body's fixed physical footprint.
                    var bed = BedGeometry.Read(interior)[0];
                    if (Mathf.Abs(corner.x) > bed.Width * 0.5f + 0.001f ||
                        Mathf.Abs(corner.z) > bed.Length * 0.5f + 0.001f || Mathf.Abs(corner.y) > 0.001f)
                        throw new InvalidOperationException("Sleeping composite extends beyond mattress: " + actor.Resident.Sex);
                }
                observedViews.Add(index);
                if (Mathf.Abs(Vector3.Dot(direction, actor.transform.right)) > 0.99f)
                { sidePosition = game.PlayerMotor.transform.position; found = true; }
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.persistentDataPath,
                    "LivingCities-sleeping" + suffix + "-position-" + direction + "-" + distance + ".png"));
                yield return null;
            }
            if (!found || !hasRotation) throw new InvalidOperationException("Sleeping fixture needs a visible bedside position.");
            game.PlayerMotor.CancelMovement = true;
            game.PlayerMotor.transform.position = sidePosition;
            yield return new WaitForSecondsRealtime(0.5f);
            AimCamera(target);
            yield return null; yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(Application.persistentDataPath, "LivingCities-sleeping" + suffix + ".png"));
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
