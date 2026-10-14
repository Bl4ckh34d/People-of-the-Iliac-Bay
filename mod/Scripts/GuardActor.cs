using System;
using System.Collections;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Utility;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    // A physical view of one saved guard. The owning controller supplies time and
    // scene geometry; visibility never creates a new identity or revives a death.
    public sealed partial class GuardActor : MonoBehaviour
    {
        public string GuardId { get; private set; }
        private GuardRoster roster;
        private TownGeometry geometry;
        private DaggerfallEntity entity;
        private MobileUnit mobile;
        private double displayedAt;
        private float groundOffset;
        private Coroutine combatStart;
        private GameObject torchBody;
        private Light torchLight;
        public void SetTorch(bool visible, bool illuminate, float range)
        {
            if (visible && !torchBody)
            {
                torchBody = new GameObject("Living Cities carried torch");
                torchBody.transform.SetParent(transform, false);
                torchBody.transform.localPosition = new Vector3(0.38f, 0.3f, 0.15f);
                var flame = GameObjectHelper.CreateDaggerfallBillboardGameObject(210, 17, torchBody.transform);
                flame.transform.localPosition = Vector3.zero;
                var size = flame.GetComponent<DaggerfallBillboard>().Summary.Size;
                flame.transform.localScale = Vector3.one * (0.8f / Mathf.Max(0.01f, size.y));
                foreach (var collider in flame.GetComponents<Collider>()) collider.enabled = false;
                torchLight = torchBody.AddComponent<Light>();
                torchLight.type = LightType.Point;
                torchLight.color = new Color(1f, 0.65f, 0.28f);
                torchLight.intensity = 1.25f;
                torchLight.shadows = LightShadows.None;
                torchLight.renderMode = LightRenderMode.Auto;
            }
            if (!torchBody) return;
            torchLight.range = Mathf.Clamp(range, 3f, 12f);
            torchLight.enabled = visible && illuminate;
            torchBody.SetActive(visible);
        }
        public bool CombatReady { get; private set; }
        public bool IsInterior { get; private set; }
        private int? buildingKey;
        private static GuardPoint3 Point(Vector3 p) { return new GuardPoint3 { X = p.x, Y = p.y, Z = p.z }; }
        private Vector3 Scene(GuardPoint3 p) { return transform.parent.TransformPoint(new Vector3((float)p.X, (float)p.Y, (float)p.Z)); }
        public static GuardActor CreateInterior(GuardRoster roster, string id, Transform parent, double now)
        { return Create(roster, id, parent, null, now); }

        public static GuardActor Create(GuardRoster roster, string id, Transform parent, TownGeometry geometry, double now)
        {
            if (roster == null || parent == null) throw new ArgumentNullException();
            var guard = roster.Guards.Find(g => g.Id == id);
            if (guard == null) throw new ArgumentException("Unknown guard identity.");
            if (!guard.IsAlive) return null;
            var point = roster.Position(id, now);
            bool indoors = guard.Interior != null;
            if (!indoors && geometry == null) throw new ArgumentNullException("geometry");
            var indoorPoint = indoors ? roster.InteriorPosition(id, now) : null;
            var ground = indoors ? parent.TransformPoint(new Vector3((float)indoorPoint.X, (float)indoorPoint.Y, (float)indoorPoint.Z)) - Vector3.up :
                geometry.ScenePoint(new RoutePoint(point.X, point.Z));
            var randomState = UnityEngine.Random.state;
            GameObject body = null;
            try
            {
                UnityEngine.Random.InitState(unchecked((int)guard.AppearanceSeed));
                body = GameObjectHelper.CreateFoeGameObjects(ground + Vector3.up, MobileTypes.Knight_CityWatch, 1, MobileReactions.Passive)[0];
                body.name = "Living Cities guard " + id;
                body.transform.SetParent(parent, true);
                // Prevent native persistence from restoring an extra copy of a guard.
                // SerializableEnemy registers in Start, after this inactive setup.
                body.GetComponent<DaggerfallEnemy>().LoadID = 0;
                body.GetComponent<EnemyMotor>().enabled = false;
                body.GetComponent<EnemySenses>().enabled = false;
                body.GetComponent<EnemyAttack>().enabled = false;
                body.GetComponent<EnemySounds>().enabled = false;
                var actor = body.AddComponent<GuardActor>();
                actor.GuardId = id; actor.roster = roster; actor.geometry = geometry;
                actor.IsInterior = indoors; actor.buildingKey = indoors ? (int?)guard.Interior.BuildingKey : null;
                actor.mobile = body.GetComponentInChildren<MobileUnit>();
                actor.entity = body.GetComponent<DaggerfallEntityBehaviour>().Entity;
                actor.entity.SetHealth(Math.Max(1, (int)Math.Ceiling(actor.entity.MaxHealth * guard.HealthFraction)), true);
                actor.entity.OnDeath += actor.Died;
                var controller = body.GetComponent<CharacterController>();
                actor.groundOffset = controller.height * 0.5f - controller.center.y + controller.skinWidth;
                actor.Place(now);
                body.SetActive(true);
                return actor;
            }
            catch { if (body) Destroy(body); throw; }
            finally { UnityEngine.Random.state = randomState; }
        }

        public void Place(double now)
        {
            var guard = roster.Guards.Find(g => g.Id == GuardId);
            if (!guard.IsAlive) { gameObject.SetActive(false); return; }
            if (IsInterior)
            {
                if (guard.Interior == null) return;
                if (!guard.Interior.Returning) { Capture(now); return; }
                var indoor = roster.InteriorPosition(GuardId, now);
                if (indoor == null) return;
                var next = Scene(indoor); var facing = next - transform.position; facing.y = 0;
                if (facing.sqrMagnitude > 0.00000001f) transform.forward = facing.normalized;
                transform.position = next;
                if (mobile.EnemyState != MobileStates.Move) mobile.ChangeEnemyState(MobileStates.Move);
                displayedAt = now; return;
            }
            if (guard.Duty == GuardDuty.Engaged) { Capture(now); return; }
            var point = roster.Position(GuardId, now);
            var position = geometry.ScenePoint(new RoutePoint(point.X, point.Z)) + Vector3.up * groundOffset;
            Vector3 direction = position - transform.position; direction.y = 0;
            if (direction.sqrMagnitude > 0.00000001f) transform.forward = direction.normalized;
            transform.position = position;
            bool moving = guard.Duty == GuardDuty.Responding || guard.Duty == GuardDuty.Returning ||
                roster.IsPatrolMoving(GuardId, now) ||
                (guard.Duty == GuardDuty.Searching && roster.Reports.Exists(r => r.Id == guard.IncidentId && r.SearchRoute != null));
            var state = moving ? MobileStates.Move : MobileStates.Idle;
            if (mobile.EnemyState != state) mobile.ChangeEnemyState(state);
            displayedAt = now;
        }

        public void BeginCombat(double now)
        {
            if (DefenseTarget) EndCombat();
            if (combatStart != null || CombatReady) return;
            var guard = roster.Guards.Find(g => g.Id == GuardId);
            if (IsInterior && guard.Interior != null && guard.Interior.LastSeenSuspect == null)
                guard.Interior.LastSeenSuspect = Point(transform.parent.InverseTransformPoint(GameManager.Instance.PlayerMotor.transform.position));
            if (guard.Duty != GuardDuty.Engaged)
            {
                roster.Engage(GuardId, now);
                var local = transform.parent.InverseTransformPoint(GameManager.Instance.PlayerMotor.transform.position);
                guard.LastSeenSuspect = new SavedRoutePoint { X = local.x, Z = local.z };
            }
            combatStart = StartCoroutine(StartCombat());
        }
        private IEnumerator StartCombat()
        {
            GetComponent<EnemyMotor>().enabled = true;
            GetComponent<EnemySenses>().enabled = true;
            yield return null; // Native Start initializes its motor before hostility is assigned.
            GetComponent<EnemyMotor>().MakeEnemyHostileToAttacker(GameManager.Instance.PlayerEntityBehaviour);
            var guard = roster.Guards.Find(g => g.Id == GuardId);
            var last = IsInterior ? Scene(guard.Interior.LastSeenSuspect) :
                geometry.ScenePoint(new RoutePoint(guard.LastSeenSuspect.X, guard.LastSeenSuspect.Z)) + Vector3.up;
            var senses = GetComponent<EnemySenses>();
            senses.OldLastKnownTargetPos = last; senses.LastKnownTargetPos = last; senses.PredictedTargetPos = last;
            SayHaltOnce(GameManager.Instance.PlayerEntityBehaviour);
            GetComponent<EnemyAttack>().enabled = true;
            CombatReady = true; combatStart = null;
        }
        public void Capture(double now)
        {
            var guard = roster.Guards.Find(g => g.Id == GuardId);
            if (!guard.IsAlive || entity.CurrentHealth <= 0) return;
            guard.HealthFraction = (double)entity.CurrentHealth / entity.MaxHealth;
            if (!IsInterior && DefenseTarget) { CaptureDefense(now); displayedAt = now; return; }
            if (IsInterior)
            {
                if (guard.Interior == null || guard.Interior.Returning) return;
                var game = GameManager.Instance; var senses = GetComponent<EnemySenses>();
                bool same = game.PlayerEnterExit.IsPlayerInsideBuilding && game.PlayerEnterExit.Interior &&
                    game.PlayerEnterExit.Interior.EntryDoor.buildingKey == buildingKey;
                bool sees = same && senses.Target == game.PlayerEntityBehaviour && senses.TargetInSight &&
                    !game.PlayerEntity.IsInvisible && CivilianResponses.Sees(transform.position, game.PlayerMotor.transform.position, 20);
                roster.ObserveInterior(GuardId, Point(transform.parent.InverseTransformPoint(transform.position)), guard.HealthFraction,
                    now, sees, sees ? Point(transform.parent.InverseTransformPoint(game.PlayerMotor.transform.position)) : null);
                displayedAt = now; return;
            }
            if (guard.Duty == GuardDuty.Engaged)
            {
                var local = transform.parent.InverseTransformPoint(transform.position);
                var player = GameManager.Instance.PlayerEntityBehaviour;
                var senses = GetComponent<EnemySenses>();
                bool sees = !GameManager.Instance.PlayerEnterExit.IsPlayerInside && senses.Target == player && senses.TargetInSight && !GameManager.Instance.PlayerEntity.IsInvisible &&
                    CivilianResponses.Sees(transform.position, player.transform.position, 20);
                var suspect = transform.parent.InverseTransformPoint(player.transform.position);
                roster.ObserveEngagement(GuardId, new SavedRoutePoint { X = local.x, Z = local.z }, guard.HealthFraction, now, sees,
                    sees ? new SavedRoutePoint { X = suspect.x, Z = suspect.z } : null);
            }
            displayedAt = now;
        }
        public void EndCombat()
        {
            ClearDefenseTarget();
            if (combatStart != null) { StopCoroutine(combatStart); combatStart = null; }
            GetComponent<EnemyMotor>().enabled = false;
            GetComponent<EnemyMotor>().IsHostile = false;
            GetComponent<EnemySenses>().enabled = false;
            GetComponent<EnemySenses>().Target = null;
            GetComponent<EnemySenses>().SecondaryTarget = null;
            GetComponent<EnemyAttack>().enabled = false;
            GetComponent<EnemySounds>().enabled = false;
            CombatReady = false;
        }

        private void Died(DaggerfallEntity ignored)
        {
            SetTorch(false, false, 8f);
            var guard = roster.Guards.Find(g => g.Id == GuardId);
            if (IsInterior && guard.Interior != null)
                guard.Interior.Position = Point(transform.parent.InverseTransformPoint(transform.position));
            else if (guard.Duty == GuardDuty.Engaged)
            {
                var local = transform.parent.InverseTransformPoint(transform.position);
                guard.EngagementPosition = new SavedRoutePoint { X = local.x, Z = local.z };
            }
            roster.Kill(GuardId, displayedAt);
            Debug.Log("LIVING_CITIES_GUARD_DEATH: " + GuardId);
        }

        private void OnDestroy()
        { if (entity != null) entity.OnDeath -= Died; }
    }
}
