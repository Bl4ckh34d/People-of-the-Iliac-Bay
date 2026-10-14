using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    // Generated residents witness violence and report it locally to the finite guard adapter.
    public sealed class CivilianResponses
    {
        private readonly WorldStateStore store;
        private readonly int settlementId;
        private float nextWarnings;
        private readonly Action<CivilianIncident, Vector3> tellGuards;
        public CivilianResponses(WorldStateStore store, int settlementId, Action<CivilianIncident, Vector3> tellGuards = null)
        { this.store = store; this.settlementId = settlementId; this.tellGuards = tellGuards; }
        public AlarmPerson Active(string id, double now)
        {
            var alarm = store.ActiveAlarm(id, now);
            return alarm == null ? null : alarm.People.Find(p => p.ResidentId == id);
        }
        public void Threat(DaggerfallEntityBehaviour target,string threatId,IEnumerable<ResidentActor> actors,double now,int building,Func<string,bool> sleeping)
        {
            CivilianIncident alarm=null;
            foreach(var actor in actors)
            {
                if(!actor || !actor.Resident.IsAlive || actor.IsSleeping || sleeping(actor.Resident.Id) || !actor.gameObject.activeInHierarchy ||
                    actor.Resident.IsQuestProtected || !Sees(actor.transform.position,target.transform.position,20)) continue;
                if(alarm==null)
                {
                    alarm=store.RecordHostileThreat(settlementId,threatId,now);
                    if(!alarm.HasLocation)
                    {
                        var point=actor.transform.parent.InverseTransformPoint(target.transform.position-Vector3.up);
                        store.LocateViolence(alarm.Id,building,point.x,point.y,point.z);
                    }
                }
                var person=store.Witness(alarm.Id,actor.Resident.Id,now);
                Locate(person,actor,building);
                if(tellGuards!=null) tellGuards(alarm,actor.transform.position);
            }
        }
        public void Murder(ResidentActor victim, IEnumerable<ResidentActor> actors, Func<string, bool> sleeping, double now, int building)
        {
            var alarm = store.RecordViolence(settlementId, victim.Resident.Id, now);
            if (!alarm.HasLocation)
            {
                var point = victim.transform.parent.InverseTransformPoint(victim.transform.position - Vector3.up);
                store.LocateViolence(alarm.Id, building, point.x, point.y, point.z);
            }
            if (tellGuards != null) tellGuards(alarm, victim.transform.position);
            foreach (var actor in actors)
            {
                if (!actor || actor == victim || !actor.Resident.IsAlive || !actor.gameObject.activeInHierarchy ||
                    (building >= 0 && actor.transform.parent != victim.transform.parent)) continue;
                var person = Observe(store, alarm, actor.Resident.Id, actor.transform.position,
                    victim.transform.position, sleeping(actor.Resident.Id) && Active(actor.Resident.Id, now) == null, now, building);
                Locate(person, actor, building);
            }
            Debug.Log("LIVING_CITIES_VIOLENCE: " + alarm.Id + "; witnesses=" + alarm.People.Count);
        }
        public void WarnNearby(IEnumerable<ResidentActor> actors, double now, int building, Func<string, bool> sleeping)
        {
            if (Time.unscaledTime < nextWarnings || GameManager.IsGamePaused) return;
            nextWarnings = Time.unscaledTime + 0.25f;
            var nearby = new List<ResidentActor>(actors);
            foreach (var actor in nearby)
            {
                if (!actor || !actor.Resident.IsAlive || !actor.gameObject.activeInHierarchy) continue;
                var alarm = store.ActiveAlarm(actor.Resident.Id, now);
                if (alarm == null) continue;
                var witness = alarm.People.Find(p => p.ResidentId == actor.Resident.Id);
                if (!witness.DirectWitness || witness.Reaction != CivilianReaction.Flee) continue;
                if (tellGuards != null) tellGuards(alarm, actor.transform.position);
                if (witness.Warned.Count >= 2) continue;
                // Sort a separate candidate list: never mutate the collection being enumerated.
                var candidates = new List<ResidentActor>(nearby);
                candidates.Sort((a, b) => Distance(actor, a).CompareTo(Distance(actor, b)));
                foreach (var other in candidates)
                {
                    if (!other || other == actor || !other.Resident.IsAlive || !other.gameObject.activeInHierarchy || sleeping(other.Resident.Id) ||
                        !Sees(actor.transform.position, other.transform.position, 5)) continue;
                    if (store.Warn(alarm.Id, actor.Resident.Id, other.Resident.Id, now))
                        Locate(alarm.People.Find(p => p.ResidentId == other.Resident.Id), other, building);
                }
            }
        }
        public static AlarmPerson Observe(WorldStateStore world, CivilianIncident alarm, string id,
            Vector3 from, Vector3 victim, bool sleeping, double now, int building)
        {
            if (id == alarm.VictimId) return null;
            AlarmPerson person = null;
            if (building >= 0)
            {
                int skill = Mathf.Clamp((int)GameManager.Instance.PlayerEntity.Skills.GetLiveSkillValue(
                    DaggerfallConnect.DFCareer.Skills.Stealth), 0, 100);
                int roll = sleeping && !alarm.NoiseChecks.Exists(n => n.ResidentId == id) ? UnityEngine.Random.Range(0,100) : 0;
                person = world.HearViolence(alarm.Id, id, building, now, sleeping, skill, Vector3.Distance(from, victim), roll);
            }
            // A sleeper awakened by a lethal blow hears the disturbance; seeing
            // its aftermath alone does not establish the attacker's identity.
            if (!sleeping && !GameManager.Instance.PlayerEntity.IsInvisible && Sees(from, victim, 20) &&
                Sees(from, GameManager.Instance.PlayerMotor.transform.position, 20))
                person = world.Witness(alarm.Id, id, now);
            return person;
        }
        private static float Distance(ResidentActor a, ResidentActor b)
        { return b ? (a.transform.position - b.transform.position).sqrMagnitude : float.PositiveInfinity; }
        private static void Locate(AlarmPerson person, ResidentActor actor, int building)
        {
            if (person == null || person.HasPosition) return;
            var local = actor.transform.parent.InverseTransformPoint(actor.IsSleeping ? actor.transform.position : actor.transform.position - Vector3.up);
            person.HasPosition = true; person.BuildingKey = building;
            person.X = local.x; person.Y = local.y; person.Z = local.z;
        }
        public static bool Sees(Vector3 from, Vector3 to, float range)
        {
            Vector3 delta = to - from;
            if (delta.sqrMagnitude > range * range) return false;
            foreach (var hit in Physics.RaycastAll(from, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
                if (Blocker(hit.collider)) return false;
            return true;
        }
        private static bool Blocker(Collider collider)
        {
            return collider.GetComponentInParent<DaggerfallEntityBehaviour>() == null &&
                collider.GetComponentInParent<StaticNPC>() == null &&
                collider.GetComponentInParent<MobilePersonNPC>() == null && collider.GetComponentInParent<PlayerMotor>() == null;
        }
        public static void Move(ResidentActor actor, AlarmPerson person, ResidentRoutine routine, Transform origin,
            TownGeometry geometry, InteriorGeometry indoor,Vector3? danger=null)
        {
            Vector3 ground = origin.TransformPoint(new Vector3((float)person.X, (float)person.Y, (float)person.Z));
            Vector3 target = ground;
            var game = GameManager.Instance;
            Vector3 player = danger ?? game.PlayerMotor.transform.position;
            if (person.Reaction == CivilianReaction.Fight)
            {
                if (!game.PlayerEntity.IsInvisible && Sees(ground + Vector3.up, player, 20))
                {
                    target = new Vector3(player.x, ground.y, player.z);
                    if (Vector3.Distance(ground + Vector3.up, player) < 1.8f)
                    {
                        target = ground;
                        if (!GameManager.IsGamePaused && Time.time >= actor.NextCounterattack)
                        {
                            actor.NextCounterattack = Time.time + 1.5f;
                            actor.Counterattacks++;
                            if (!game.PlayerEntity.GodMode) game.PlayerEntity.DecreaseHealth(2 + (int)(actor.Resident.AppearanceSeed % 4));
                        }
                    }
                }
            }
            else if (person.BuildingKey == -1 && routine.Route.Count >= 2)
            {
                // Retrace this resident's collision-validated commute to their own door.
                float best = float.PositiveInfinity; int segment = 1;
                Vector3 local = origin.InverseTransformPoint(ground);
                for (int i = 1; i < routine.Route.Count; i++)
                {
                    Vector3 a = Point(routine.Route[i - 1]), b = Point(routine.Route[i]);
                    Vector3 d = b - a; float fraction = d.sqrMagnitude < 0.00001f ? 0 : Mathf.Clamp01(Vector3.Dot(local - a, d) / d.sqrMagnitude);
                    float distance = (local - (a + d * fraction)).sqrMagnitude;
                    if (distance < best) { best = distance; segment = i; }
                }
                int index = segment - 1;
                if (Vector3.Distance(new Vector3(local.x, 0, local.z), Point(routine.Route[index])) < 0.15f && index > 0) index--;
                target = geometry.ScenePoint(new RoutePoint(routine.Route[index].X, routine.Route[index].Z));
                if (index == 0 && Vector3.Distance(ground, target) < 0.2f)
                { person.ReachedRefuge = true; person.BuildingKey = actor.Resident.HomeKey; }
            }
            else if (indoor != null)
            {
                // Conservative room-level retreat. Door/stair traversal comes with the indoor alarm adapter.
                float best = (ground + Vector3.up - player).sqrMagnitude;
                foreach (var anchor in indoor.LocalAnchors)
                {
                    var candidate = origin.TransformPoint(anchor);
                    float distance = (candidate + Vector3.up - player).sqrMagnitude;
                    if (distance > best && Sees(ground + Vector3.up, candidate + Vector3.up, 15)) { target = candidate; best = distance; }
                }
            }
            float speed = person.Reaction == CivilianReaction.Flee ? (float)ResidentMovement.FleeSpeed(actor.Resident.AppearanceSeed) : 2.8f;
            Vector3 deltaMove = Vector3.MoveTowards(ground, target, GameManager.IsGamePaused ? 0 : speed * Time.deltaTime) - ground;
            if (indoor != null) InteriorGeometry.OpenOrdinaryDoors(ground, ground + deltaMove);
            bool blocked = false;
            if (deltaMove.sqrMagnitude > 0.000001f)
                foreach (var hit in Physics.CapsuleCastAll(ground + Vector3.up * 0.3f, ground + Vector3.up * 1.65f,
                    0.22f, deltaMove.normalized, deltaMove.magnitude, ~0, QueryTriggerInteraction.Ignore))
                    if (Blocker(hit.collider)) { blocked = true; break; }
            if (!blocked && deltaMove.sqrMagnitude > 0.000001f)
            {
                Vector3 proposed = ground + deltaMove;
                bool supported = false;
                foreach (var hit in Physics.RaycastAll(proposed + Vector3.up * 0.4f, Vector3.down, 0.8f, ~0, QueryTriggerInteraction.Ignore))
                    if (Blocker(hit.collider) && hit.normal.y > 0.85f && Mathf.Abs(hit.point.y - ground.y) < 0.3f)
                    { proposed.y = hit.point.y; supported = true; break; }
                if (supported) ground = proposed; else blocked = true;
            }
            actor.Place(ground, person.Reaction == CivilianReaction.Fight ? player - ground : deltaMove, blocked || deltaMove.sqrMagnitude < 0.000001f);
            var saved = origin.InverseTransformPoint(ground); person.X = saved.x; person.Y = saved.y; person.Z = saved.z;
        }
        private static Vector3 Point(SavedRoutePoint point) { return new Vector3((float)point.X, 0, (float)point.Z); }
    }
}
