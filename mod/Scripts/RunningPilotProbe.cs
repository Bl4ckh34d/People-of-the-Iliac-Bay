using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Utility.ModSupport;
using DaggerfallWorkshop.Game.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Actual-player fixture for the selected complete Archive 385 running set.
    // It owns one disposable actor and never changes a saved resident's wardrobe.
    public static class RunningPilotProbe
    {
        public static IEnumerator Run()
        {
            var game = GameManager.Instance;
            var mod = ModManager.Instance.GetModFromGUID("e527783b-394c-4f43-b5f4-0cabd3d05dd4");
            if (!game || mod == null) throw new System.InvalidOperationException("Running pilot requires loaded People of the Iliac Bay mod.");
            var directory = game.StreamingWorld.GetCurrentBuildingDirectory();
            var navigation = game.StreamingWorld.GetCurrentCityNavigation();
            if (!directory || !navigation) throw new System.InvalidOperationException("Running pilot requires loaded exterior city.");

            Texture2D atlas = RunningPilotPose.Load(mod);
            var sleepingArt = new SleepingArt(mod);
            GameObject body = null;
            ResidentActor actor = null;
            try
            {
                body = UnityEngine.Object.Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject, directory.transform);
                body.name = "Disposable People of the Iliac Bay Archive 385 running pilot fixture";
                Vector3 from, to;
                if (!FindClearLane(directory, navigation, out from, out to))
                    throw new System.InvalidOperationException("No clear street lane for running pilot fixture.");
                Vector3 runDirection = (to - from).normalized;
                body.transform.position = from - runDirection * 2f + Vector3.up;
                body.transform.rotation = Quaternion.LookRotation(runDirection);
                actor = body.AddComponent<ResidentActor>();
                var resident = new Resident
                {
                    Id = "running-pilot-probe",
                    FullName = "Running Pilot Fixture",
                    AgeGroup = ResidentAgeGroup.Adult,
                    Race = (int)Races.Breton,
                    Sex = ResidentSex.Male,
                    AppearanceSeed = 0,
                    IsAlive = true,
                };
                actor.Initialize(resident, 0, navigation);
                actor.SetRunningPilotAtlas(atlas);
                var nativeRenderer = actor.Person.Asset.GetComponent<MeshRenderer>();
                if (!nativeRenderer || actor.Person.IsGuard || actor.Person.Race != Races.Breton ||
                    actor.Person.Gender != Genders.Male || actor.Person.PersonOutfitVariant != 0 ||
                    !(actor.Person.Asset is MobilePersonBillboard))
                    throw new System.InvalidOperationException("Disposable fixture did not produce native Archive 385 appearance.");

                // Enter flee state by moving forward from the near end of the lane.
                // Keep the actor's heading fixed while the camera orbits so each
                // camera sample maps to a distinct native orientation.
                actor.Place(from, runDirection, false, true);
                yield return new WaitForEndOfFrame();
                var pose = body.GetComponent<CompletedNpcPose>();
                if (!pose || !pose.Active || !nativeRenderer.forceRenderingOff)
                    throw new System.InvalidOperationException("Active flee movement did not replace native renderer.");

                var orientations = new HashSet<int>();
                var frames = new HashSet<int>();
                var screenshots = new List<string>();
                string screenshotRoot = Application.persistentDataPath;
                string screenshotId = Guid.NewGuid().ToString("N");
                Vector3 previousPosition = actor.transform.position;
                float movementDistance = 0;
                for (int step = 0; step < 8; step++)
                {
                    Vector3 radial = Quaternion.AngleAxis(step * 45f, Vector3.up) * actor.transform.forward;
                    // The movement owner is normally called every frame. Sparse
                    // snapshots alone cannot exercise its bounded physical step.
                    float until=Time.realtimeSinceStartup+0.12f;
                    do
                    {
                        actor.Place(to,runDirection,false,true);
                        movementDistance+=Vector3.Distance(previousPosition,actor.transform.position);
                        previousPosition=actor.transform.position;
                        game.PlayerMotor.transform.position=actor.transform.position+radial*4f+Vector3.up*0.2f;
                        SleepProbe.AimCamera(nativeRenderer.bounds.center);
                        yield return null;
                        if(pose.Active)frames.Add(pose.Frame);
                    }while(Time.realtimeSinceStartup<until);
                    yield return new WaitForEndOfFrame();
                    if (!pose.Active || !nativeRenderer.forceRenderingOff || pose.Orientation < 0 || pose.Orientation > 7)
                        throw new System.InvalidOperationException("Archive 385 preview failed at angle "+step+"; active="+pose.Active+
                            "; suppressed="+nativeRenderer.forceRenderingOff+"; standing="+actor.IsStandingStill+
                            "; moved="+movementDistance+"; remaining="+Vector3.Distance(actor.transform.position-Vector3.up,to));
                    if (!orientations.Add(pose.Orientation))
                        throw new System.InvalidOperationException("Camera orbit repeated a running-pilot orientation.");
                    frames.Add(pose.Frame);
                    string screenshot = Path.Combine(screenshotRoot,
                        "PeopleOfTheIliacBay-running-pilot-" + screenshotId + "-angle-" + pose.Orientation + ".png");
                    screenshots.Add(screenshot);
                    ScreenCapture.CaptureScreenshot(screenshot);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_RUNNING_PILOT_ANGLE: orientation=" + pose.Orientation + "; frame=" + pose.Frame);
                }
                if (orientations.Count != 8)
                    throw new System.InvalidOperationException("Running pilot did not cover all eight native orientations.");
                if (frames.Count != 4)
                    throw new System.InvalidOperationException("Running pilot did not display all four atlas frames during movement.");
                if (movementDistance < 1f)
                    throw new System.InvalidOperationException("Running pilot orientation samples did not include sustained actor movement: "+movementDistance);
                float screenshotDeadline = Time.realtimeSinceStartup + 10f;
                while (screenshots.Exists(path => !File.Exists(path)) && Time.realtimeSinceStartup < screenshotDeadline)
                    yield return null;
                if (screenshots.Exists(path => !File.Exists(path) || new FileInfo(path).Length == 0))
                    throw new System.InvalidOperationException("Running pilot orientation screenshots were not written.");

                actor.SetRunningPreview(false);
                if (pose.Active || nativeRenderer.forceRenderingOff || !nativeRenderer.enabled)
                    throw new System.InvalidOperationException("Stopping preview did not restore native renderer.");

                actor.Person.Asset.IsIdle = false;
                if (!actor.SetRunningPreview(true)) throw new System.InvalidOperationException("Conversation restore setup failed.");
                actor.SetConversationFacing(true);
                if (pose.Active || nativeRenderer.forceRenderingOff || !nativeRenderer.enabled)
                    throw new System.InvalidOperationException("Talking did not stop and restore running preview.");
                actor.SetConversationFacing(false);
                actor.Person.Asset.IsIdle = false;

                Vector3 current = actor.transform.position;
                actor.Place(current - Vector3.up, Vector3.forward, true);
                if (pose.Active || actor.SetRunningPreview(true) || !actor.IsStandingStill)
                    throw new System.InvalidOperationException("Standing actor accepted running preview.");

                var sleepAtlas = sleepingArt.ForResident(resident);
                if (sleepAtlas == null) throw new System.InvalidOperationException("Archive 385 sleep fixture art missing.");
                actor.Sleep(directory.transform, new BedAnchor
                {
                    LocalCenter = directory.transform.InverseTransformPoint(current),
                    LocalHeadDirection = Vector3.forward,
                    Length = 2f,
                    Width = 1f,
                }, sleepAtlas);
                if (!actor.IsSleeping || actor.SetRunningPreview(true) || pose.Active)
                    throw new System.InvalidOperationException("Sleeping actor accepted running preview.");
                body.GetComponent<SleepingPose>().Wake();

                actor.Place(actor.transform.position - Vector3.up + Vector3.forward * 2f, Vector3.forward);
                actor.Person.Asset.IsIdle = false;
                if (!actor.SetRunningPreview(true)) throw new System.InvalidOperationException("Death restore setup failed.");
                body.GetComponent<DaggerfallEntityBehaviour>().Entity.CurrentHealth = 0;
                if (resident.IsAlive || body.activeInHierarchy || pose.Active || !nativeRenderer.enabled)
                    throw new System.InvalidOperationException("Death did not stop preview and restore native renderer.");

                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_RUNNING_PILOT_PASS: archive=385; orientations=" + orientations.Count +
                    "; frames=" + frames.Count + "; restored=conversation,standing,sleep,death; screenshots=" + screenshotRoot);
            }
            finally
            {
                if (actor) actor.SetRunningPreview(false);
                if (actor && actor.IsSleeping) actor.GetComponent<SleepingPose>().Wake();
                if (body) { body.SetActive(false); UnityEngine.Object.Destroy(body); }
                sleepingArt.Dispose();
                UnityEngine.Object.Destroy(atlas);
            }
        }

        private static bool FindClearLane(BuildingDirectory directory, CityNavigation navigation,
            out Vector3 from, out Vector3 to)
        {
            var geometry = new TownGeometry(directory, navigation);
            foreach (var entrance in geometry.Entrances.Values)
                foreach (var other in geometry.Entrances.Values)
                {
                    if (entrance.BuildingKey == other.BuildingKey ||
                        Vector3.Distance(entrance.LocalDoor, other.LocalDoor) > 60) continue;
                    var route = geometry.RouteBetween(entrance, other);
                    for (int i = 0; i < route.Count - 1; i++)
                    {
                        Vector3 a = geometry.ScenePoint(route[i]);
                        Vector3 heading = (geometry.ScenePoint(route[i + 1]) - a).normalized;
                        for (int j = i + 1; j < route.Count; j++)
                        {
                            Vector3 b = geometry.ScenePoint(route[j]);
                            if (Vector3.Dot((b - a).normalized, heading) < 0.99f) break;
                            if (Vector3.Distance(a, b) < 8f) continue;
                            Vector3 middle = (a + b) * 0.5f;
                            Vector3 side = Vector3.Cross(Vector3.up, heading).normalized;
                            if (!Wide(middle - heading * 3f, side) || !Wide(middle, side) ||
                                !Wide(middle + heading * 3f, side)) continue;
                            from = middle - heading * 3f;
                            to = middle + heading * 3f;
                            return true;
                        }
                    }
                }
            from = to = Vector3.zero;
            return false;
        }

        private static bool Wide(Vector3 point, Vector3 side)
        {
            foreach (float offset in new[] { -1.5f, 0f, 1.5f })
            {
                Vector3 target = point + side * offset;
                foreach (var collider in Physics.OverlapCapsule(target + Vector3.up * 0.91f,
                    target + Vector3.up * 1.65f, 0.4f, ~0, QueryTriggerInteraction.Ignore))
                    if (Static(collider)) return false;
                if (offset == 0) continue;
                foreach (var hit in Physics.CapsuleCastAll(point + Vector3.up * 0.91f,
                    point + Vector3.up * 1.65f, 0.4f, side * Mathf.Sign(offset), Mathf.Abs(offset),
                    ~0, QueryTriggerInteraction.Ignore))
                    if (Static(hit.collider)) return false;
            }
            return true;
        }

        private static bool Static(Collider collider)
        {
            return !collider.GetComponentInParent<MobilePersonNPC>() &&
                !collider.GetComponentInParent<StaticNPC>() &&
                !collider.GetComponentInParent<DaggerfallEntityBehaviour>() &&
                !collider.GetComponentInParent<PlayerMotor>();
        }
    }
}
