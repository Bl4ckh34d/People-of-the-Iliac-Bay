using System;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    // A short physical jump to an already requested, identified dungeon floor.
    // No generated links, remote approach points, moving platforms or teleports.
    internal sealed class NativeDungeonJumpTraversal : MonoBehaviour
    {
        private const float Skin = 0.015f;
        private const float StepSeconds = 0.02f;
        private readonly RaycastHit[] hits = new RaycastHit[64];
        private readonly Collider[] overlaps = new Collider[64];
        private ResidentActor actor;
        private DaggerfallEntity entity;
        private DaggerfallDungeon dungeon;
        private NativeNavigationWorld world;
        private Vector3 lower, upper, destination, horizontalVelocity;
        private float radius, verticalVelocity, elapsed, duration, peakHeight;
        private float retryAt;
        private int advancedFrame = -1;
        private Func<Vector3, bool> permitted;
        public bool Active { get; private set; }
        public float Progress {get{return duration>0?Mathf.Clamp01(elapsed/duration):0;}}
        public bool ReachedDestination { get; private set; }
        public float DistanceTravelled { get; private set; }
        public string Outcome { get; private set; }

        public static bool TryStart(NativeResidentNavigation navigation, Vector3 requested,
            double speed, Func<Vector3, bool> permitted)
        {
            var game = GameManager.Instance;
            if (!navigation || !game || !game.PlayerEnterExit ||
                !game.PlayerEnterExit.IsPlayerInsideDungeon || GameManager.IsGamePaused) return false;
            var jump = navigation.GetComponent<NativeDungeonJumpTraversal>();
            if (!jump) jump = navigation.gameObject.AddComponent<NativeDungeonJumpTraversal>();
            if (jump.Active) return true;
            if (Time.unscaledTime < jump.retryAt) return false;
            jump.retryAt = Time.unscaledTime + 0.75f;
            NpcJumpTrajectory trajectory;
            Vector3 landing;
            string reason;
            if (!jump.TryPrepare(requested, speed, permitted, out landing, out trajectory, out reason))
            { jump.Outcome = reason; return false; }
            navigation.Pause();
            Vector3 ground = jump.transform.position - Vector3.up;
            jump.destination = landing;
            var direction = landing - ground; direction.y = 0;
            jump.horizontalVelocity = direction.normalized * (float)trajectory.HorizontalSpeed;
            jump.verticalVelocity = (float)trajectory.LaunchSpeed;
            jump.duration = (float)trajectory.Duration;
            jump.elapsed = 0;
            jump.peakHeight = ground.y;
            jump.permitted = permitted;
            jump.DistanceTravelled = 0;
            jump.ReachedDestination = false;
            jump.Outcome = "airborne";
            jump.Active = true;
            jump.Advance();
            return true;
        }

        internal bool TryPrepare(Vector3 requested, double speed, Func<Vector3, bool> allowed,
            out Vector3 landing, out NpcJumpTrajectory trajectory, out string reason)
        {
            landing = Vector3.zero; trajectory = null; reason = "dungeon-jump-context";
            var game = GameManager.Instance;
            actor = GetComponent<ResidentActor>();
            var behaviour = GetComponent<DaggerfallEntityBehaviour>();
            entity = behaviour ? behaviour.Entity : null;
            world = NativeNavigationWorld.Current;
            dungeon = game && game.PlayerEnterExit ? game.PlayerEnterExit.Dungeon : null;
            if (!game || !game.PlayerEnterExit || !actor || actor.Resident == null || !actor.Resident.IsAlive || actor.Resident.IsQuestProtected ||
                actor.IsConversing || actor.IsSeated || actor.IsSleeping || entity == null || entity.CurrentHealth <= 0 ||
                entity.IsParalyzed || !game.PlayerEnterExit.IsPlayerInsideDungeon || !dungeon || !world || !world.Ready ||
                world.Scene != "dungeon-" + dungeon.Summary.ID || !Finite(requested) ||
                double.IsNaN(speed) || double.IsInfinity(speed) || speed <= 0) return false;
            var capsule = GetComponent<CapsuleCollider>();
            if (!capsule || !capsule.enabled || capsule.direction != 1 ||
                (transform.lossyScale - Vector3.one).sqrMagnitude > 0.000001f ||
                Vector3.Dot(transform.up, Vector3.up) < 0.9999f) return false;
            radius = capsule.radius - Skin;
            if (radius <= 0 || capsule.height < capsule.radius * 2) return false;
            Vector3 center = transform.TransformPoint(capsule.center) - transform.position + Vector3.up;
            lower = center - Vector3.up * (capsule.height * 0.5f - capsule.radius);
            upper = center + Vector3.up * (capsule.height * 0.5f - capsule.radius);
            // This adapter uses the established resident root = ground + 1 m.
            // Refuse a replacement body whose feet do not follow that convention.
            if (Mathf.Abs(lower.y - capsule.radius) > 0.03f ||
                new Vector2(center.x, center.z).magnitude > 0.03f) return false;
            Vector3 from = transform.position - Vector3.up;
            var delta = requested - from; delta.y = 0;
            if (delta.magnitude < 0.6f || delta.magnitude > 4f || requested.y - from.y < -0.5f)
            { reason = "dungeon-jump-distance"; return false; }
            Vector3 takeoff;
            if (!Supported(from, out takeoff) || !Supported(requested, out landing))
            { reason = "dungeon-jump-floor-unverified"; return false; }
            if (landing.y - from.y < -0.5f)
            { reason = "dungeon-jump-drop"; return false; }
            NavMeshHit mesh;
            if (!NavMesh.SamplePosition(landing, out mesh, 0.25f, NavMesh.AllAreas) ||
                new Vector2(mesh.position.x - landing.x, mesh.position.z - landing.z).magnitude > 0.10f)
            { reason = "dungeon-jump-landing-off-mesh"; return false; }
            // AcrobatMotor is player-input-bound. Reuse its default constants and
            // its live-skill/athleticism/effect formula for this actual NPC body.
            float multiplier = 1f + entity.Skills.GetLiveSkillValue(DFCareer.Skills.Jumping) * 0.5f / 100f;
            if (entity.Career != null && entity.Career.Athleticism)
            {
                multiplier += 0.1f;
                if (entity.ImprovedAthleticism) multiplier += 0.1f;
            }
            if (entity.IsEnhancedJumping) multiplier += 0.6f;
            // Deliberately omit the player's forward input boost. The caller's
            // actual walking speed is a conservative maximum horizontal speed.
            if (!NpcJumpTrajectory.TryCreate(delta.magnitude, landing.y - from.y,
                AcrobatMotor.defaultJumpSpeed * multiplier, AcrobatMotor.defaultGravity, speed, out trajectory) ||
                trajectory.Duration > 1.5)
            { reason = "dungeon-jump-capability"; return false; }
            if (!ClearAt(from) || !ClearAt(landing))
            { reason = "dungeon-jump-body-occupied"; return false; }
            Vector3 previous = from;
            int steps = Mathf.CeilToInt((float)trajectory.Duration / StepSeconds);
            for (int i = 1; i <= steps; i++)
            {
                double time = trajectory.Duration * i / steps;
                Vector3 next = from + delta * (float)(time / trajectory.Duration) + Vector3.up * (float)trajectory.HeightAt(time);
                RaycastHit obstacle;
                if (allowed != null && !allowed(next) || !ClearAt(next) || Sweep(previous, next, out obstacle))
                { reason = "dungeon-jump-arc-blocked"; return false; }
                previous = next;
            }
            reason = null; return true;
        }

        // Called by the resident before any ordinary placement, and as a
        // fallback in LateUpdate so stopping a command never freezes gravity.
        public void Advance()
        {
            if (!Active || advancedFrame == Time.frameCount || GameManager.IsGamePaused) return;
            advancedFrame = Time.frameCount;
            var game = GameManager.Instance;
            if (!game || !game.PlayerEnterExit || !game.PlayerEnterExit.IsPlayerInsideDungeon ||
                !dungeon || game.PlayerEnterExit.Dungeon != dungeon || NativeNavigationWorld.Current != world ||
                !world || !world.Ready || entity == null || entity.CurrentHealth <= 0)
            { Finish(false, "dungeon-jump-context-ended"); return; }
            float maximumSpeed = horizontalVelocity.magnitude + Mathf.Abs(verticalVelocity) + AcrobatMotor.defaultGravity * 0.05f;
            float remaining = Mathf.Min(Time.deltaTime, Mathf.Min(0.05f, 0.45f / Mathf.Max(1f, maximumSpeed)));
            while (Active && remaining > 0.000001f)
            {
                float step = Mathf.Min(StepSeconds, remaining);
                // Bound physical displacement even during an interrupted fall.
                step = Mathf.Min(step, 0.25f / Mathf.Max(1f, horizontalVelocity.magnitude + Mathf.Abs(verticalVelocity)));
                if (elapsed < duration) step = Mathf.Min(step, duration - elapsed);
                remaining -= step;
                Vector3 from = transform.position - Vector3.up;
                if (entity.IsParalyzed) horizontalVelocity = Vector3.zero;
                Vector3 next = from + horizontalVelocity * step + Vector3.up *
                    (verticalVelocity * step - AcrobatMotor.defaultGravity * step * step * 0.5f);
                verticalVelocity -= AcrobatMotor.defaultGravity * step;
                elapsed += step;
                if (permitted != null && !permitted(next))
                {
                    horizontalVelocity = Vector3.zero;
                    next.x = from.x; next.z = from.z;
                    Outcome = "dungeon-jump-permission-changed";
                }
                RaycastHit hit;
                if (Sweep(from, next, out hit))
                {
                    var delta = next - from;
                    next = from + delta.normalized * Mathf.Max(0, hit.distance - Skin);
                    MoveBody(next);
                    if (hit.normal.y >= 0.7f && verticalVelocity <= 0)
                    {
                        Vector3 floor;
                        if (Supported(next, out floor))
                        {
                            bool reached = Vector3.Distance(next, destination) <= 0.4f;
                            Finish(reached, reached ? "landed" : "dungeon-jump-interrupted-landing");
                            return;
                        }
                        // Another native body or moving surface is not the
                        // verified floor. Keep gravity active until it clears.
                        verticalVelocity = 0;
                    }
                    horizontalVelocity = Vector3.zero;
                    if (hit.normal.y < -0.5f && verticalVelocity > 0) verticalVelocity = 0;
                    Outcome = "dungeon-jump-obstructed";
                }
                else if (ClearAt(next)) MoveBody(next);
                else
                {
                    // Sweeps can omit a collider that appeared intersecting
                    // their starting volume between frames. Never tunnel out.
                    horizontalVelocity = Vector3.zero;
                    if (verticalVelocity > 0) verticalVelocity = 0;
                    next = from;
                    Outcome = "dungeon-jump-body-entered-arc";
                }
                if (elapsed >= duration)
                {
                    horizontalVelocity = Vector3.zero;
                    // The inset sweep capsule leaves a small contact skin. A
                    // downward floor observation confirms landing without a snap.
                    Vector3 floor;
                    if (verticalVelocity <= 0 && Supported(next, out floor) && next.y - floor.y <= Skin * 2.5f)
                    {
                        bool reached = Vector3.Distance(next, destination) <= 0.4f;
                        Finish(reached, reached ? "landed" : "dungeon-jump-interrupted-landing");
                    }
                }
            }
        }

        private void MoveBody(Vector3 ground)
        {
            DistanceTravelled += Vector3.Distance(transform.position - Vector3.up, ground);
            peakHeight = Mathf.Max(peakHeight, ground.y);
            transform.position = ground + Vector3.up;
            var crowd = GetComponent<CrowdMotion>();
            if (crowd) crowd.RestorePlacement(ground);
        }

        private void Finish(bool reached, string outcome)
        {
            Active = false; ReachedDestination = reached; Outcome = outcome;
            retryAt = Time.unscaledTime + 1f;
            if (actor) actor.HoldPhysicalPosition();
            // Same threshold/rate as native PlayerHealth. Normally this short
            // jump cannot cause damage; an intervening body can interrupt it.
            float fall = peakHeight - (transform.position.y - 1f);
            if (outcome != "dungeon-jump-context-ended" && entity != null && fall > 5f)
                entity.DecreaseHealth((int)(5f * (fall - 5f)));
            int gain;
            if(reached && actor && entity!=null && NativeNpcCompanionExperience.TryAwardSuccessfulUse(actor.CompanionStateStore,
                actor.Resident.Id,(int)DFCareer.Skills.Jumping,ResidentActor.CompanionNow(),out gain) && gain>0)
                entity.Skills.SetPermanentSkillValue(DFCareer.Skills.Jumping,(short)Math.Min(100,
                    entity.Skills.GetPermanentSkillValue(DFCareer.Skills.Jumping)+gain));
        }

        private bool Supported(Vector3 point, out Vector3 floor)
        {
            floor = point;
            if (!FloorAt(point, out floor)) return false;
            // Check an actual body footprint, not a single ray over a thin edge.
            Vector3 ignored;
            float offset = radius * 0.8f;
            return FloorAt(point + Vector3.right * offset, out ignored) &&
                FloorAt(point - Vector3.right * offset, out ignored) &&
                FloorAt(point + Vector3.forward * offset, out ignored) &&
                FloorAt(point - Vector3.forward * offset, out ignored);
        }

        private bool FloorAt(Vector3 point, out Vector3 floor)
        {
            floor = point;
            int count = Physics.RaycastNonAlloc(point + Vector3.up * 0.20f, Vector3.down, hits, 0.4f, ~0, QueryTriggerInteraction.Collide);
            if (count == hits.Length) return false;
            int nearest = -1;
            for (int i = 0; i < count; i++)
                if (Blocks(hits[i].collider) && (nearest < 0 || hits[i].distance < hits[nearest].distance)) nearest = i;
            if (nearest < 0) return false;
            var hit = hits[nearest];
            if (hit.normal.y < 0.7f || Mathf.Abs(hit.point.y - point.y) > 0.18f ||
                !hit.collider.transform.IsChildOf(dungeon.transform) || NativeNavigationGeometry.IsDynamicNavigationBody(hit.collider)) return false;
            floor = hit.point; return true;
        }

        private bool ClearAt(Vector3 ground)
        {
            int count = Physics.OverlapCapsuleNonAlloc(ground + lower, ground + upper, radius,
                overlaps, ~0, QueryTriggerInteraction.Collide);
            if (count == overlaps.Length) return false;
            for (int i = 0; i < count; i++) if (Blocks(overlaps[i])) return false;
            return true;
        }

        private bool Sweep(Vector3 from, Vector3 to, out RaycastHit nearest)
        {
            nearest = default(RaycastHit);
            var delta = to - from;
            if (delta.sqrMagnitude < 0.000000001f) return false;
            int count = Physics.CapsuleCastNonAlloc(from + lower, from + upper, radius,
                delta.normalized, hits, delta.magnitude, ~0, QueryTriggerInteraction.Collide);
            if (count == hits.Length) return true;
            bool found = false;
            for (int i = 0; i < count; i++)
                if (Blocks(hits[i].collider) && (!found || hits[i].distance < nearest.distance))
                { nearest = hits[i]; found = true; }
            return found;
        }

        private bool Blocks(Collider collider)
        {
            if (!collider || collider.transform.IsChildOf(transform)) return false;
            // Native mobile bodies are trigger capsules. Ordinary non-body
            // triggers do not obstruct a jump; other people's bodies do.
            return !collider.isTrigger || collider.GetComponentInParent<DaggerfallEntityBehaviour>() ||
                collider.GetComponentInParent<PlayerMotor>();
        }

        private static bool Finite(Vector3 value)
        { return !float.IsNaN(value.x) && !float.IsInfinity(value.x) && !float.IsNaN(value.y) &&
            !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z); }
        private void LateUpdate() { Advance(); }
        private void OnDisable() { Active = false; }
    }
}
