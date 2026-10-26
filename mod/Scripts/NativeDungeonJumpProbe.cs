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
    // The caller must first identify two actual loaded dungeon floors. Fixture
    // coordinates are dungeon-local, never a new platform or a production warp.
    internal static class NativeDungeonJumpProbe
    {
        [Serializable]
        private sealed class Fixture
        {
            public int mapId = -1;
            public Vector3 start, end;
            public int jumpingSkill = 50;
        }

        public static IEnumerator Run()
        {
            var game = GameManager.Instance;
            string json = Environment.GetEnvironmentVariable("POTIB_DUNGEON_JUMP_FIXTURE");
            if (!game || game.PlayerEntity == null ||
                !game.PlayerEntity.Name.StartsWith("PeopleOfTheIliacBayTest-", StringComparison.Ordinal) ||
                !game.PlayerEnterExit || !game.PlayerEnterExit.IsPlayerInsideDungeon ||
                string.IsNullOrWhiteSpace(json) || json.Length > 4096)
                throw new InvalidOperationException("Dungeon jump probe requires an owned loaded dungeon and POTIB_DUNGEON_JUMP_FIXTURE JSON.");
            Fixture fixture = JsonUtility.FromJson<Fixture>(json);
            var dungeon = game.PlayerEnterExit.Dungeon;
            var world = NativeNavigationWorld.Current;
            if (fixture == null || !dungeon || fixture.mapId != dungeon.Summary.ID ||
                fixture.jumpingSkill < 0 || fixture.jumpingSkill > 100 || !Finite(fixture.start) || !Finite(fixture.end) ||
                !world || !world.Ready ||
                world.Scene != "dungeon-" + fixture.mapId)
                throw new InvalidOperationException("Dungeon jump fixture differs from the loaded native dungeon or skill bounds.");
            Vector3 start = dungeon.transform.TransformPoint(fixture.start);
            Vector3 end = dungeon.transform.TransformPoint(fixture.end);
            NavMeshHit sampled;
            if (!NavMesh.SamplePosition(start, out sampled, 0.2f, NavMesh.AllAreas) ||
                Vector3.Distance(sampled.position, start) > 0.2f)
                throw new InvalidOperationException("Jump takeoff is not an observed native NavMesh floor.");
            var walking = new NavMeshPath();
            if (NavMesh.CalculatePath(start, end, NavMesh.AllAreas, walking) && walking.status == NavMeshPathStatus.PathComplete)
                throw new InvalidOperationException("The selected floors already have a complete walking route; choose an actual disconnected gap.");
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
                Physics.SyncTransforms();
                Vector3 landing; NpcJumpTrajectory trajectory; string refusal;
                if (!jump.TryPrepare(end, actor.WalkingSpeed, null, out landing, out trajectory, out refusal))
                    throw new InvalidOperationException("Actual dungeon jump fixture refused: " + refusal);
                Vector3 before = body.transform.position;
                if (jump.TryPrepare(end + Vector3.right * 100, actor.WalkingSpeed, null, out landing, out trajectory, out refusal) ||
                    body.transform.position != before || jump.Active)
                    throw new InvalidOperationException("An unreachable jump changed the body or was accepted.");
                TownSmokeHarness.Watch(actor.transform);
                float deadline = Time.realtimeSinceStartup + 8;
                float peak = start.y, maximumStep = 0, travelled = 0;
                Vector3 previous = start;
                bool started = false;
                while (Time.realtimeSinceStartup < deadline)
                {
                    actor.Place(actor.transform.position - Vector3.up, end - actor.transform.position,
                        navigationDestination: end);
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
                    "; actualFloor=true; capsuleSweeps=true; unreachableRefused=true; generatedGeometry=false; platform=false");
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
