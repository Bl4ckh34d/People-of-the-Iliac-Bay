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
        internal static bool IsEnemyThreat(DaggerfallEntityBehaviour target)
        {
            if (!target || !target.gameObject.activeInHierarchy || target.GetComponent<GuardActor>() ||
                !(target.Entity is EnemyEntity) || target.Entity.CurrentHealth <= 0 || target.Entity.Team == MobileTeams.PlayerAlly) return false;
            var enemy = (EnemyEntity)target.Entity;
            if (enemy.MobileEnemy.ID == (int)MobileTypes.Knight_CityWatch) return false;
            var motor = target.GetComponent<EnemyMotor>();
            // Quest ownership does not excuse an active hostile attacker. The guard
            // explicitly targets this foe; preserve the quest's global infighting flag.
            return motor && motor.IsHostile;
        }
        public string RegisterHostileThreat(DaggerfallEntityBehaviour target)
        {
            if(!IsEnemyThreat(target)) return null;
            string key=ThreatKey(target); defenseEnemies[key]=target; return key;
        }
        public bool TryHostileThreatPosition(string key,out Vector3 position)
        {
            DaggerfallEntityBehaviour target;
            if(key!=null && defenseEnemies.TryGetValue(key,out target) && IsEnemyThreat(target))
            { position=target.transform.position; return true; }
            position=Vector3.zero; return false;
        }
        private void ReportHostileThreat(CivilianIncident alarm,Vector3 from,double now)
        {
            if(!OwnsTown() || !EnemyDefense || GameManager.Instance.PlayerEnterExit.IsPlayerInside) return;
            if(alarm.SettlementId!=directory.MapID || !alarm.HasLocation || now<alarm.StartedAt || now>=CivilianAlarms.ActiveUntil(alarm)) return;
            DaggerfallEntityBehaviour target;
            if(!defenseEnemies.TryGetValue(alarm.ThreatId,out target) || !IsEnemyThreat(target) || ObserveHauntingThreat(target,now)) return;
            if(Roster.Reports.Exists(r=>r.ThreatId==alarm.ThreatId && Roster.Guards.Exists(g=>g.IsAlive && g.IncidentId==r.Id))) return;
            var local=directory.transform.InverseTransformPoint(target.transform.position);
            var destination=new SavedRoutePoint { X=local.x,Z=local.z };
            var routes=new Dictionary<string,List<SavedRoutePoint>>();
            foreach(var guard in Roster.Guards)
            {
                if(!guard.IsAlive || guard.Duty!=GuardDuty.Posted || guard.Interior!=null) continue;
                var origin=Roster.Position(guard.Id,now);
                if(!CivilianResponses.Sees(from,geometry.ScenePoint(new RoutePoint(origin.X,origin.Z))+Vector3.up,20)) continue;
                var route=geometry.RouteBetweenPoints(origin,destination);
                if(route.Count>=2) routes.Add(guard.Id,route);
            }
            Roster.DispatchDefense("danger:"+alarm.Id,alarm.ThreatId,now,routes,Mathf.Clamp(MaximumResponders,1,2),2.8,DaggerfallUnity.Instance.WorldTime.TimeScale);
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
                var sent = Roster.DispatchDefense(report,key,now,routes,Mathf.Clamp(MaximumResponders,1,2),2.8,DaggerfallUnity.Instance.WorldTime.TimeScale);
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
                if(EnemyDefense && !valid && TryDefenseWelfare(guard,actor,report,now)) return true;
                actor.EndCombat();
                if (routeTick)
                {
                    var current = Local(actor.transform.position);
                    var route = geometry.RouteBetweenPoints(current,guard.Post);
                    if (route.Count >= 2)
                    {
                        if (guard.Duty != GuardDuty.Engaged) Roster.Engage(guard.Id,now);
                        Roster.ObserveEngagement(guard.Id,current,guard.HealthFraction,now,false);
                        Roster.ReturnFromEngagement(guard.Id,now,route,2.8,DaggerfallUnity.Instance.WorldTime.TimeScale);
                    }
                }
                return true;
            }
            if (guard.Duty == GuardDuty.Engaged || CivilianResponses.Sees(actor.transform.position,target.transform.position,20))
                actor.BeginDefenseCombat(target,now);
            return true;
        }
        private void RefreshInteriorThreats()
        {
            defenseEnemies.Clear();
            foreach(var target in UnityEngine.Object.FindObjectsOfType<DaggerfallEntityBehaviour>())
                if(IsEnemyThreat(target)) defenseEnemies[ThreatKey(target)]=target;
        }
        private bool UpdateInteriorEnemyDefense(GuardRecord guard,GuardActor actor,GuardReport report,double now)
        {
            if(report==null || report.ThreatId==null) return false;
            actor.Capture(now);
            // A real assault on the guard remains a player crime even while it
            // is defending against someone else, matching exterior behavior.
            if(actor.DefenseTarget && actor.GetComponent<EnemyMotor>().IsHostile)
            {
                if(GameManager.Instance.PlayerEntity.CrimeCommitted==PlayerEntity.Crimes.None)
                    GameManager.Instance.PlayerEntity.CrimeCommitted=PlayerEntity.Crimes.Assault;
                // Native assault already identifies the attacker. Do not discard that
                // knowledge and require a second sighting after ending enemy combat.
                var attacker=interior.transform.InverseTransformPoint(GameManager.Instance.PlayerMotor.transform.position);
                guard.Interior.LastSeenSuspect=new GuardPoint3 { X=attacker.x,Y=attacker.y,Z=attacker.z };
                actor.EndCombat(); report.ThreatId=null; actor.BeginCombat(now); return true;
            }
            DaggerfallEntityBehaviour target;
            defenseEnemies.TryGetValue(report.ThreatId,out target);
            if(!EnemyDefense || !IsEnemyThreat(target) || now>=guard.SearchUntil)
            {
                if(EnemyDefense && !IsEnemyThreat(target) && TryDefenseWelfare(guard,actor,report,now)) return true;
                actor.EndCombat();
                if(GameManager.Instance.PlayerEntity.CrimeCommitted!=PlayerEntity.Crimes.None)
                { report.ThreatId=null; return false; }
                Roster.EndInteriorSearch(guard.Id,now); actor.Place(now);
                return true;
            }
            if(CivilianResponses.Sees(actor.transform.position,target.transform.position,20))
                actor.BeginDefenseCombat(target,now);
            else if(!actor.CombatReady && guard.Interior.LastSeenSuspect!=null)
                actor.SearchInterior(interior,now);
            return true;
        }
    }
}
