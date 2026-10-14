using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using LivingCities.Core;
using UnityEngine;

namespace LivingCities.DFU
{
    public sealed partial class GuardPopulation
    {
        public bool EnemyDefense = true;
        private readonly Dictionary<string,DaggerfallEntityBehaviour> defenseEnemies = new Dictionary<string,DaggerfallEntityBehaviour>();
        private readonly string defenseSession = Guid.NewGuid().ToString("N");
        private string ThreatKey(DaggerfallEntityBehaviour target)
        {
            var enemy = target.GetComponent<DaggerfallEnemy>();
            return enemy && enemy.LoadID != 0 ? "native:" + enemy.LoadID : "session:" + defenseSession + ":" + target.GetInstanceID();
        }
        private static bool IsEnemyThreat(DaggerfallEntityBehaviour target)
        {
            if (!target || !target.gameObject.activeInHierarchy || target.GetComponent<GuardActor>() ||
                !(target.Entity is EnemyEntity) || target.Entity.CurrentHealth <= 0 || target.Entity.Team == MobileTeams.PlayerAlly) return false;
            var enemy = (EnemyEntity)target.Entity;
            if (enemy.MobileEnemy.ID == (int)MobileTypes.Knight_CityWatch) return false;
            var motor = target.GetComponent<EnemyMotor>(); var senses = target.GetComponent<EnemySenses>();
            return motor && motor.IsHostile && (!senses || !senses.QuestBehaviour || senses.QuestBehaviour.IsAttackableByAI);
        }
        private void SurveyEnemyThreats(double now)
        {
            defenseEnemies.Clear();
            foreach (var target in UnityEngine.Object.FindObjectsOfType<DaggerfallEntityBehaviour>())
                if (!ObserveHauntingThreat(target,now) && IsEnemyThreat(target)) defenseEnemies[ThreatKey(target)] = target;
            if (!EnemyDefense) return;
            var keys = new List<string>(defenseEnemies.Keys); keys.Sort(StringComparer.Ordinal);
            foreach (var key in keys)
            {
                if (Roster.Reports.Exists(r => r.ThreatId == key && Roster.Guards.Exists(g => g.IsAlive && g.IncidentId == r.Id))) continue;
                var target = defenseEnemies[key]; var local = directory.transform.InverseTransformPoint(target.transform.position);
                var destination = new SavedRoutePoint { X = local.x, Z = local.z };
                var routes = new Dictionary<string,List<SavedRoutePoint>>();
                foreach (var guard in Roster.Guards)
                {
                    if (!guard.IsAlive || guard.Duty != GuardDuty.Posted || guard.Interior != null) continue;
                    var origin = Roster.Position(guard.Id,now);
                    if (!CivilianResponses.Sees(geometry.ScenePoint(new RoutePoint(origin.X,origin.Z)) + Vector3.up,target.transform.position,20)) continue;
                    var route = geometry.RouteBetweenPoints(origin,destination);
                    if (route.Count >= 2) routes.Add(guard.Id,route);
                }
                if (routes.Count == 0) continue;
                string report = "defense:" + key + ":" + now.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
                var sent = Roster.DispatchDefense(report,key,now,routes,Mathf.Clamp(MaximumResponders,1,2),2.8,12);
                if (sent.Count > 0) Debug.Log("LIVING_CITIES_ENEMY_DEFENSE: target=" + key + "; guards=" + sent.Count);
            }
        }
        private bool UpdateEnemyDefense(GuardRecord guard, GuardActor actor, double now, bool routeTick)
        {
            var report = Roster.Reports.Find(r => r.Id == guard.IncidentId);
            if (report == null || report.ThreatId == null || guard.Duty == GuardDuty.Returning) return false;
            DaggerfallEntityBehaviour target;
            defenseEnemies.TryGetValue(report.ThreatId,out target);
            bool valid = EnemyDefense && IsEnemyThreat(target);
            if (guard.Duty != GuardDuty.Engaged) actor.Place(now);
            else actor.Capture(now);
            if (!valid || now >= guard.SearchUntil)
            {
                actor.EndCombat();
                if (routeTick)
                {
                    var current = Local(actor.transform.position);
                    var route = geometry.RouteBetweenPoints(current,guard.Post);
                    if (route.Count >= 2)
                    {
                        if (guard.Duty != GuardDuty.Engaged) Roster.Engage(guard.Id,now);
                        Roster.ObserveEngagement(guard.Id,current,guard.HealthFraction,now,false);
                        Roster.ReturnFromEngagement(guard.Id,now,route,2.8,12);
                    }
                }
                return true;
            }
            if (guard.Duty == GuardDuty.Engaged || CivilianResponses.Sees(actor.transform.position,target.transform.position,20))
                actor.BeginDefenseCombat(target,now);
            return true;
        }
    }
}
