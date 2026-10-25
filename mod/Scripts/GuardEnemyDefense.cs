using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class GuardPopulation
    {
        public bool EnemyDefense = true;
        private readonly Dictionary<string,DaggerfallEntityBehaviour> defenseEnemies = new Dictionary<string,DaggerfallEntityBehaviour>();
        private readonly string defenseSession = Guid.NewGuid().ToString("N");
        private string ThreatKey(DaggerfallEntityBehaviour target)
        {
            var resident=target ? target.GetComponent<ResidentActor>() : null;
            if(resident!=null && resident.Resident!=null && !string.IsNullOrEmpty(resident.Resident.Id)) return resident.Resident.Id;
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
            var evidence=target.GetComponent<GuardAttackEvidence>();
            return motor && (motor.IsHostile || evidence && evidence.Until>=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds());
        }
        private bool IsWitnessedResidentAssault(DaggerfallEntityBehaviour target,double now)
        {
            var evidence=target?target.GetComponent<GuardAttackEvidence>():null;
            var scene=target?DefenseScene.For(target):null;
            if(evidence==null || !evidence.ResidentAssault || evidence.Until<now || scene==null || scene.Root!=evidence.SceneRoot || !evidence.Victim)return false;
            if(evidence.Victim==GameManager.Instance.PlayerEntityBehaviour)return scene.PlayerPresent;
            var victimScene=DefenseScene.For(evidence.Victim);
            return victimScene!=null && victimScene.Root==evidence.SceneRoot;
        }
        private bool IsDefenseTarget(DaggerfallEntityBehaviour target,double now)
        {
            if(!target || !target.gameObject.activeInHierarchy || target.Entity==null || target.Entity.CurrentHealth<=0 ||
                target.GetComponent<GuardActor>()) return false;
            bool witnessed=IsWitnessedResidentAssault(target,now);
            if(worldStore!=null && !witnessed)
            {
                var record=worldStore.GetDefenseCombatant(ThreatKey(target));
                if(record!=null && record.PlayerProvoked)return false;
            }
            return witnessed || IsEnemyThreat(target);
        }
        public string RegisterHostileThreat(DaggerfallEntityBehaviour target)
        {
            if(!IsDefenseTarget(target,Now())) return null;
            string key=ThreatKey(target); defenseEnemies[key]=target; return key;
        }
        public bool TryHostileThreatPosition(string key,out Vector3 position)
        {
            DaggerfallEntityBehaviour target;
            if(key!=null && defenseEnemies.TryGetValue(key,out target) && !IsWitnessedResidentAssault(target,Now()) &&
                IsDefenseTarget(target,Now()) && !target.Entity.IsInvisible)
            { position=target.transform.position; return true; }
            position=Vector3.zero; return false;
        }
        private void ReportHostileThreat(CivilianIncident alarm,Vector3 from,double now)
        {
            if(!OwnsTown() || !EnemyDefense || GameManager.Instance.PlayerEnterExit.IsPlayerInside) return;
            if(alarm.SettlementId!=directory.MapID || !alarm.HasLocation || now<alarm.StartedAt || now>=CivilianAlarms.ActiveUntil(alarm)) return;
            DaggerfallEntityBehaviour target;
            if(!defenseEnemies.TryGetValue(alarm.ThreatId,out target) || !IsDefenseTarget(target,now) || target.Entity.IsInvisible ||
                IsWitnessedResidentAssault(target,now) || ObserveHauntingThreat(target,now) || SharedMurderResponse(alarm.ThreatId)) return;
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
            ReconcileEnemyMurderResolution();
            defenseEnemies.Clear();
            foreach (var target in UnityEngine.Object.FindObjectsOfType<DaggerfallEntityBehaviour>())
                if (!ObserveHauntingThreat(target,now) && IsDefenseTarget(target,now)) defenseEnemies[ThreatKey(target)] = target;
            if (!EnemyDefense) return;
            var keys = new List<string>(defenseEnemies.Keys); keys.Sort(StringComparer.Ordinal);
            foreach (var key in keys)
            {
                // This evidence authorizes only guards that saw the strike. Keep
                // its target live for that report, but never let a later survey
                // disclose its position to unrelated guards.
                if(IsWitnessedResidentAssault(defenseEnemies[key],now)) continue;
                if(defenseEnemies[key].Entity.IsInvisible || SharedMurderResponse(key))continue;
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
                if (sent.Count > 0) Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ENEMY_DEFENSE: target=" + key + "; guards=" + sent.Count);
            }
        }
        private bool SharedMurderResponse(string key)
        {
            if(worldStore==null || !directory)return false;
            foreach(var alarm in worldStore.OutdoorCorpses(directory.MapID))
                if(!alarm.CrimeResolved && alarm.KillerThreatId==key &&
                    Roster.Reports.Exists(r=>r.Id=="outdoor:"+alarm.Id &&
                        Roster.Guards.Exists(g=>g.IsAlive && g.IncidentId==r.Id && g.Duty!=GuardDuty.Posted && g.Duty!=GuardDuty.Returning)))return true;
            return false;
        }
        private bool UpdateEnemyDefense(GuardRecord guard, GuardActor actor, double now, bool routeTick)
        {
            if(UpdateArcherHelp(guard,actor,now,routeTick)) return true;
            var report = Roster.Reports.Find(r => r.Id == guard.IncidentId);
            if (report == null || report.ThreatId == null || guard.Duty == GuardDuty.Returning) return false;
            DaggerfallEntityBehaviour target;
            defenseEnemies.TryGetValue(report.ThreatId,out target);
            bool valid = EnemyDefense && (IsDefenseTarget(target,now) || IsKnownMurdererDefense(guard,report,target));
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
            if(routeTick && !target.Entity.IsInvisible && guard.Duty!=GuardDuty.Returning && guard.BowSupportGuardId==null &&
                CivilianResponses.Sees(actor.transform.position,target.transform.position,EnemyAttack.maxRangedDistance))
            {
                GuardArchery.EnsureLoadout(guard);
                var ground=Local(target.transform.position);
                if((!guard.HasBow || guard.ArrowsRemaining==0) && target.transform.position.y-
                    geometry.ScenePoint(new RoutePoint(ground.X,ground.Z)).y>3 && TrySendForArcher(guard,report.ThreatId,now))
                {actor.EndCombat();return true;}
            }
            if (!target.Entity.IsInvisible && (guard.Duty == GuardDuty.Engaged || CivilianResponses.Sees(actor.transform.position,target.transform.position,20)))
                actor.BeginDefenseCombat(target,now);
            return true;
        }
        private void RefreshInteriorThreats()
        {
            ReconcileEnemyMurderResolution();
            defenseEnemies.Clear();
            foreach(var target in UnityEngine.Object.FindObjectsOfType<DaggerfallEntityBehaviour>())
                if(IsDefenseTarget(target,Now())) defenseEnemies[ThreatKey(target)]=target;
        }
        private bool UpdateInteriorEnemyDefense(GuardRecord guard,GuardActor actor,GuardReport report,double now)
        {
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
                actor.EndCombat(); if(report!=null)report.ThreatId=null; actor.BeginCombat(now); return true;
            }
            if(report==null || report.ThreatId==null) return UpdateWitnessedInteriorAssault(guard,actor,now);
            DaggerfallEntityBehaviour target;
            defenseEnemies.TryGetValue(report.ThreatId,out target);
            if(!EnemyDefense || !IsDefenseTarget(target,now) || now>=guard.SearchUntil)
            {
                if(EnemyDefense && !IsDefenseTarget(target,now) && TryDefenseWelfare(guard,actor,report,now)) return true;
                actor.EndCombat();
                if(GameManager.Instance.PlayerEntity.CrimeCommitted!=PlayerEntity.Crimes.None)
                { report.ThreatId=null; return false; }
                Roster.EndInteriorSearch(guard.Id,now); actor.Place(now);
                return true;
            }
            if(!target.Entity.IsInvisible && CivilianResponses.Sees(actor.transform.position,target.transform.position,20))
                actor.BeginDefenseCombat(target,now);
            else if(!actor.CombatReady && guard.Interior.LastSeenSuspect!=null)
                actor.SearchInterior(interior,now);
            return true;
        }
    }
}
