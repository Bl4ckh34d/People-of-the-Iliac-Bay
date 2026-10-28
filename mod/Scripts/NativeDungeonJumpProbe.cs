using System;
using System.Collections;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    // Explicit coordinates or the bounded native survey identify actual floors.
    // Fixture placement is test setup, never a production warp or new platform.
    internal static partial class NativeDungeonJumpProbe
    {
        [Serializable]
        private sealed class Fixture
        {
            public int mapId = -1;
            public Vector3 start, end;
            public int jumpingSkill = 50;
            public bool enterPrivateersHold = false, discoverFloors = false;
            public bool verifyFollowTarget = true;
        }

        public static IEnumerator Run()
        {
            var game = GameManager.Instance;
            string json = Environment.GetEnvironmentVariable("POTIB_DUNGEON_JUMP_FIXTURE");
            if (!game || game.PlayerEntity == null ||
                !NativeProbeText.IsTestCharacter(game.PlayerEntity.Name) ||
                !game.PlayerEnterExit ||
                string.IsNullOrWhiteSpace(json) || json.Length > 4096)
                throw new InvalidOperationException("Dungeon jump probe requires an owned loaded dungeon and POTIB_DUNGEON_JUMP_FIXTURE JSON.");
            Fixture fixture = JsonUtility.FromJson<Fixture>(json);
            if (fixture != null && fixture.enterPrivateersHold)
            {
                if (fixture.mapId != 187853213)
                    throw new InvalidOperationException("Explicit jump fixture transport accepts only actual Privateer's Hold.");
                yield return NativeDungeonModuleProbe.Enter();
            }
            var dungeon = game.PlayerEnterExit.Dungeon;
            float readyDeadline = Time.realtimeSinceStartup + 20;
            while (game.PlayerEnterExit.IsPlayerInsideDungeon && Time.realtimeSinceStartup < readyDeadline &&
                (!dungeon || !NativeNavigationWorld.Current || !NativeNavigationWorld.Current.Ready ||
                    NativeNavigationWorld.Current.Scene != "dungeon-" + dungeon.Summary.ID)) yield return null;
            var world = NativeNavigationWorld.Current;
            if (fixture == null || !game.PlayerEnterExit.IsPlayerInsideDungeon || !dungeon || !dungeon.StartMarker || fixture.mapId != dungeon.Summary.ID ||
                fixture.jumpingSkill < 0 || fixture.jumpingSkill > 100 || !Finite(fixture.start) || !Finite(fixture.end) ||
                !world || !world.Ready ||
                world.Scene != "dungeon-" + fixture.mapId)
                throw new InvalidOperationException("Dungeon jump fixture differs from the loaded native dungeon or skill bounds.");
            Vector3 start = fixture.discoverFloors ? dungeon.StartMarker.transform.position : dungeon.transform.TransformPoint(fixture.start);
            Vector3 end = dungeon.transform.TransformPoint(fixture.end);
            var body = UnityEngine.Object.Instantiate(DaggerfallUnity.Instance.Option_MobileNPCPrefab.gameObject);
            try
            {
                body.name = "Native dungeon jump resident trial";
                body.transform.SetParent(dungeon.transform, true);
                body.transform.position = start + Vector3.up;
                var actor = body.AddComponent<ResidentActor>();
                actor.Initialize(new Resident { Id = "native-dungeon-jump-trial", FullName = "Dungeon Jumper",
                    AppearanceSeed = 52, Sex = ResidentSex.Male, AgeGroup = ResidentAgeGroup.Adult, IsAlive = true },
                    1, game.StreamingWorld.GetCurrentCityNavigation());
                var entity = body.GetComponent<DaggerfallEntityBehaviour>().Entity;
                entity.Skills.SetPermanentSkillValue(DFCareer.Skills.Jumping, (short)fixture.jumpingSkill);
                var crowd = body.GetComponent<CrowdMotion>();
                crowd.RestorePlacement(start);
                var jump = body.AddComponent<NativeDungeonJumpTraversal>();
                if (fixture.discoverFloors)
                {
                    bool found = false;
                    yield return DiscoverFloors(actor, jump, (from, to) => { start = from; end = to; found = true; });
                    if (!found) throw new InvalidOperationException("No actual disconnected static dungeon floors support the bounded jump; inspect DUNGEON_JUMP_SURVEY.");
                    fixture.start = dungeon.transform.InverseTransformPoint(start);
                    fixture.end = dungeon.transform.InverseTransformPoint(end);
                }
                NavMeshHit sampled;
                if (!NavMesh.SamplePosition(start, out sampled, 0.2f, NavMesh.AllAreas) ||
                    Vector3.Distance(sampled.position, start) > 0.2f)
                    throw new InvalidOperationException("Jump takeoff is not an observed native NavMesh floor.");
                var walking = new NavMeshPath();
                if (NavMesh.CalculatePath(start, end, NavMesh.AllAreas, walking) && walking.status == NavMeshPathStatus.PathComplete)
                    throw new InvalidOperationException("The selected floors already have a complete walking route; choose an actual disconnected gap.");
                crowd.RestorePlacement(start);
                Physics.SyncTransforms();
                Vector3 landing; NpcJumpTrajectory trajectory; string refusal;
                if (!jump.TryPrepare(end, actor.WalkingSpeed, null, out landing, out trajectory, out refusal))
                    throw new InvalidOperationException("Actual dungeon jump fixture refused: " + refusal);
                Vector3 before = body.transform.position;
                if (jump.TryPrepare(end + Vector3.right * 100, actor.WalkingSpeed, null, out landing, out trajectory, out refusal) ||
                    body.transform.position != before || jump.Active)
                    throw new InvalidOperationException("An unreachable jump changed the body or was accepted.");
                if (fixture.verifyFollowTarget)
                {
                    bool standing;
                    NativeDungeonFollowTarget.Resolve(actor, end, out standing);
                    if (standing) throw new InvalidOperationException("Dungeon Follow treats a disconnected landing as arrived from air distance alone.");
                }
                TownSmokeHarness.Watch(actor.transform);
                float deadline = Time.realtimeSinceStartup + 8;
                float peak = start.y, maximumStep = 0, travelled = 0;
                Vector3 previous = start;
                bool started = false;
                while (Time.realtimeSinceStartup < deadline)
                {
                    bool standing = false;
                    var target = fixture.verifyFollowTarget ? NativeDungeonFollowTarget.Resolve(actor, end, out standing) : end;
                    actor.Place(actor.transform.position - Vector3.up, end - actor.transform.position,
                        standing: standing, navigationDestination: target);
                    Vector3 current = actor.transform.position - Vector3.up;
                    float step = Vector3.Distance(current, previous);
                    maximumStep = Mathf.Max(maximumStep, step); travelled += step;
                    peak = Mathf.Max(peak, current.y); previous = current;
                    started |= jump.Active || jump.ReachedDestination;
                    if (maximumStep > 0.51f)
                        throw new InvalidOperationException("Dungeon jump moved more than the bounded physical frame step.");
                    if (started && !jump.Active) break;
                    yield return null;
                }
                if (!started || jump.Active || !jump.ReachedDestination || jump.Outcome != "landed" ||
                    peak - start.y < 0.2f || travelled < 0.6f || Vector3.Distance(previous, end) > 0.4f)
                    throw new InvalidOperationException("Dungeon jump did not physically reach its floor: " + jump.Outcome);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_JUMP_PROBE_PASS: map=" + fixture.mapId +
                    "; jumpingSkill=" + fixture.jumpingSkill + "; localStart=" + fixture.start.ToString("F4") +
                    "; localEnd=" + fixture.end.ToString("F4") + "; peakRise=" + (peak - start.y) +
                    "; travelled=" + travelled + "; maximumStep=" + maximumStep +
                    "; actualFloor=true; capsuleSweeps=true; unreachableRefused=true; followTarget=" + fixture.verifyFollowTarget +
                    "; generatedGeometry=false; platform=false");
            }
            finally
            {
                TownSmokeHarness.Watch(null);
                body.SetActive(false); UnityEngine.Object.Destroy(body);
            }
        }

        private static bool Finite(Vector3 value)
        { return !float.IsNaN(value.x) && !float.IsInfinity(value.x) && !float.IsNaN(value.y) &&
            !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z); }
    }
}
