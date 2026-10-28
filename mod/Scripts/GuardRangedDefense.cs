using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
namespace PeopleOfTheIliacBay.DFU
{
    public sealed class GuardAttackEvidence : MonoBehaviour
    {
        public double Until;
        // This is set only after an observed nonlethal resident hit. It must not
        // turn generic threat scans into an unseen-assault location channel.
        public bool ResidentAssault;
        public Transform SceneRoot;
        public DaggerfallEntityBehaviour Victim;
    }
    public sealed partial class GuardPopulation
    {
        private readonly Dictionary<string,float> nextArcherSearch=new Dictionary<string,float>();
        private readonly Dictionary<string,string> witnessedInteriorAssaults=new Dictionary<string,string>();
        public void ObserveResidentAssault(ResidentActor victim,DaggerfallEntityBehaviour attacker)
        { ObserveBodyAssault(victim?victim.GetComponent<DaggerfallEntityBehaviour>():null,attacker); }
        public void ObservePlayerAssault(DaggerfallEntityBehaviour attacker)
        { ObserveBodyAssault(GameManager.Instance.PlayerEntityBehaviour,attacker); }
        // Effect hook calls only after native BaseEntityEffect.Start accepted a
        // hostile non-health effect. It shares the same direct-witness boundary.
        public void ObserveResidentHostileEffect(ResidentActor victim,DaggerfallEntityBehaviour attacker)
        { ObserveBodyAssault(victim?victim.GetComponent<DaggerfallEntityBehaviour>():null,attacker); }
        public void ObserveResidentHostileMagic(ResidentActor victim,DaggerfallEntityBehaviour attacker)
        { ObserveBodyAssault(victim?victim.GetComponent<DaggerfallEntityBehaviour>():null,attacker); }
        public void ObserveBodyHostileMagic(DaggerfallEntityBehaviour victim,DaggerfallEntityBehaviour attacker)
        { ObserveBodyAssault(victim,attacker); }
        public void ObserveBodyAssault(DaggerfallEntityBehaviour victim,DaggerfallEntityBehaviour attacker)
        {
            if(!EnemyDefense || !OwnsTown() || !victim || !attacker || attacker.Entity==null || attacker.Entity.CurrentHealth<=0 ||
                attacker.GetComponent<GuardActor>() || attacker.Entity.Team==MobileTeams.PlayerAlly) return;
            var victimResident=victim.GetComponent<ResidentActor>();
            if(victimResident!=null && (victimResident.Resident==null || victimResident.Resident.IsQuestProtected))return;
            var sourceResident=attacker.GetComponent<ResidentActor>();
            if(sourceResident!=null && (sourceResident.Resident==null || sourceResident.Resident.IsQuestProtected ||
                SourceIsAuthorizedDefense(sourceResident,victim))) return;
            var sourceScene=DefenseScene.For(attacker);var victimScene=DefenseScene.For(victim);
            bool playerVictim=victim==GameManager.Instance.PlayerEntityBehaviour;
            if(sourceScene==null || (!playerVictim && victimScene==null) || (playerVictim && !sourceScene.PlayerPresent) ||
                (!playerVictim && (victimScene.SettlementId!=sourceScene.SettlementId || victimScene.Root!=sourceScene.Root)) ||
                sourceScene.SettlementId!=directory.MapID || attacker.Entity.IsInvisible) return;
            double now=Now();
            if(worldStore!=null)
            {
                var record=worldStore.GetDefenseCombatant(ThreatKey(attacker));
                if(record!=null && record.PlayerProvoked && playerVictim)return;
            }
            // Callers establish successful native damage/effect first. These
            // sight checks preserve source and victim without turning a global
            // aggression bit into remote knowledge.
            if(!CivilianResponses.Sees(victim.transform.position,attacker.transform.position,20))return;
            var evidence=attacker.GetComponent<GuardAttackEvidence>() ?? attacker.gameObject.AddComponent<GuardAttackEvidence>();
            evidence.Until=now+300;evidence.ResidentAssault=true;evidence.SceneRoot=sourceScene.Root;evidence.Victim=victim;
            string key=ThreatKey(attacker);defenseEnemies[key]=attacker;
            foreach(var pair in new List<KeyValuePair<string,GuardActor>>(actors))
            {
                var guardActor=pair.Value;var guard=Roster.Guards.Find(g=>g.Id==pair.Key);
                if(!guardActor || !guardActor.gameObject.activeInHierarchy || guard==null || !guard.IsAlive || guard.Refuge!=null ||
                    guardActor.IsInterior!=(sourceScene.Interior!=null) || !CivilianResponses.Sees(guardActor.transform.position,victim.transform.position,20) ||
                    !CivilianResponses.Sees(guardActor.transform.position,attacker.transform.position,20))continue;
                if(guardActor.IsInterior)
                {
                    if(guard.Interior==null || guard.Interior.Returning || guard.Interior.BuildingKey!=sourceScene.BuildingKey)continue;
                    witnessedInteriorAssaults[guard.Id]=key;guardActor.BeginDefenseCombat(attacker,now);continue;
                }
                if(guard.Duty!=GuardDuty.Posted || guard.Interior!=null || guard.BowSupportGuardId!=null)continue;
                var origin=Roster.Position(guard.Id,now);var atPost=new List<SavedRoutePoint>{origin,origin};
                string report="resident-assault:"+guard.Id+":"+now.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
                if(Roster.DispatchDefense(report,key,now,new Dictionary<string,List<SavedRoutePoint>>{{guard.Id,atPost}},1,2.8,
                    DaggerfallUnity.Instance.WorldTime.TimeScale).Count>0)guardActor.BeginDefenseCombat(attacker,now);
            }
        }
        private bool SourceIsAuthorizedDefense(ResidentActor source,DaggerfallEntityBehaviour victim)
        {
            if(worldStore==null || source==null || source.Resident==null || !victim)return false;
            var action=worldStore.ActiveNpcAction(source.Resident.Id);
            var native=victim.GetComponent<DaggerfallEnemy>();
            string target=ThreatKey(victim);
            return action!=null && !NpcActionExecutor.IsTerminal(action) &&
                (action.Kind==NpcActionKind.Attack || action.Kind==NpcActionKind.Defend) && action.Target!=null &&
                action.Target.Kind==NpcActionTargetKind.Person && action.Target.PersonId==target && native && native.LoadID!=0 &&
                NativeNpcCombatAssist.Unprovoked(worldStore,target,directory.MapID);
        }
        private bool UpdateWitnessedInteriorAssault(GuardRecord guard,GuardActor actor,double now)
        {
            string key;
            if(!witnessedInteriorAssaults.TryGetValue(guard.Id,out key))return false;
            DaggerfallEntityBehaviour target;
            defenseEnemies.TryGetValue(key,out target);
            if(!EnemyDefense || !IsDefenseTarget(target,now) || target.Entity.IsInvisible || guard.Interior==null || guard.Interior.Returning)
            { witnessedInteriorAssaults.Remove(guard.Id);actor.EndCombat();return false; }
            if(CivilianResponses.Sees(actor.transform.position,target.transform.position,20))actor.BeginDefenseCombat(target,now);
            return true;
        }
        public void ObserveGuardAttack(GuardActor victim,DaggerfallEntityBehaviour attacker)
        {
            if(!OwnsTown() || !victim || !attacker || attacker.Entity==null || attacker.Entity.CurrentHealth<=0 ||
                attacker.GetComponent<GuardActor>()) return;
            GuardActor owned;
            if(!actors.TryGetValue(victim.GuardId,out owned) || owned!=victim) return;
            double now=Now();
            var guard=Roster.Guards.Find(g=>g.Id==victim.GuardId);
            if(guard==null || !guard.IsAlive)return;
            bool player=attacker==GameManager.Instance.PlayerEntityBehaviour;
            if(player && GameManager.Instance.PlayerEntity.CrimeCommitted==PlayerEntity.Crimes.None)
                GameManager.Instance.PlayerEntity.CrimeCommitted=PlayerEntity.Crimes.Assault;
            if(!player && (!EnemyDefense || attacker.Entity.Team==MobileTeams.PlayerAlly))return;
            if(guard.Refuge!=null)
            {
                if(guard.Refuge.WallRoute!=null && guard.Refuge.Evaluate(now).Phase==RefugePhase.WallPosted)
                    victim.RememberWallAttacker(attacker);
                return;
            }
            if(player || interior)return; // Ordinary native player combat remains authoritative.
            var evidence=attacker.GetComponent<GuardAttackEvidence>() ?? attacker.gameObject.AddComponent<GuardAttackEvidence>();
            evidence.Until=now+300;
            string key=ThreatKey(attacker);defenseEnemies[key]=attacker;
            if(guard==null || !guard.IsAlive || guard.Interior!=null || guard.Refuge!=null || guard.BowSupportGuardId!=null) return;
            // A hit identifies aggression, but a hidden shooter is not a known location.
            if(!CivilianResponses.Sees(victim.transform.position,attacker.transform.position,EnemyAttack.maxRangedDistance)) return;
            if(guard.Duty!=GuardDuty.Posted) return;
            GuardArchery.EnsureLoadout(guard);
            var origin=Roster.Position(guard.Id,now);
            var target=Local(attacker.transform.position);
            bool elevated=attacker.transform.position.y-geometry.ScenePoint(new RoutePoint(target.X,target.Z)).y>3;
            var route=elevated ? new List<SavedRoutePoint>() : geometry.RouteBetweenPoints(origin,target);
            if((elevated || route.Count<2) && (!guard.HasBow || guard.ArrowsRemaining==0) &&
                TrySendForArcher(guard,key,now)) return;
            // Directly engage a visible attacker without demanding a route onto its roof.
            var atPost=new List<SavedRoutePoint>{origin,origin};
            string report="hit:"+guard.Id+":"+now.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
            if(Roster.DispatchDefense(report,key,now,new Dictionary<string,List<SavedRoutePoint>>{{guard.Id,atPost}},1,2.8,
                DaggerfallUnity.Instance.WorldTime.TimeScale).Count>0) victim.BeginDefenseCombat(attacker,now);
        }
        private bool TrySendForArcher(GuardRecord runner,string threat,double now)
        {
            float next;
            if(nextArcherSearch.TryGetValue(runner.Id,out next) && Time.unscaledTime<next) return false;
            nextArcherSearch[runner.Id]=Time.unscaledTime+5;
            var origin=Roster.Position(runner.Id,now);
            var candidates=new List<GuardRecord>(Roster.Guards);
            candidates.RemoveAll(g=>!g.IsAlive || g.Interior!=null || g.Refuge!=null || g.Duty!=GuardDuty.Posted);
            candidates.Sort((a,b)=>{
                var x=Roster.Position(a.Id,now);var y=Roster.Position(b.Id,now);
                return ((x.X-origin.X)*(x.X-origin.X)+(x.Z-origin.Z)*(x.Z-origin.Z)).CompareTo(
                    (y.X-origin.X)*(y.X-origin.X)+(y.Z-origin.Z)*(y.Z-origin.Z));});
            foreach(var helper in candidates)
            {
                GuardArchery.EnsureLoadout(helper);
                if(helper.Id==runner.Id || !helper.IsAlive || !helper.HasBow || helper.ArrowsRemaining<=0 ||
                    helper.Duty!=GuardDuty.Posted || helper.Interior!=null || helper.Refuge!=null) continue;
                if(Roster.Guards.Exists(g=>g.IsAlive && g.Id!=runner.Id && g.BowSupportGuardId==helper.Id)) continue;
                var route=geometry.RouteBetweenPoints(origin,Roster.Position(helper.Id,now));
                if(route.Count<2) continue;
                string report="archer-help:"+runner.Id+":"+now.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
                if(runner.Duty==GuardDuty.Posted)
                {
                    if(Roster.DispatchDefense(report,threat,now,new Dictionary<string,List<SavedRoutePoint>>{{runner.Id,route}},1,2.8,
                        DaggerfallUnity.Instance.WorldTime.TimeScale).Count==0) continue;
                }
                else
                {
                    var home=geometry.RouteBetweenPoints(route[route.Count-1],runner.Post);
                    if(home.Count<2) continue;
                    Roster.RedirectDefense(runner.Id,now,route,home,2.8,DaggerfallUnity.Instance.WorldTime.TimeScale);
                }
                runner.BowSupportGuardId=helper.Id;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GUARD_SEEKS_ARCHER: runner="+runner.Id+"; archer="+helper.Id);
                return true;
            }
            return false;
        }
        private bool UpdateArcherHelp(GuardRecord runner,GuardActor actor,double now,bool routeTick)
        {
            if(runner.BowSupportGuardId==null) return false;
            if(runner.Duty!=GuardDuty.Responding && runner.Duty!=GuardDuty.Searching)
            {runner.BowSupportGuardId=null;return false;}
            actor.EndCombat();actor.Place(now);
            if(routeTick) RecruitSeenDefenseGuards(runner,actor,now);
            if(!routeTick || now<runner.ArrivesAt) return true;
            var helper=Roster.Guards.Find(g=>g.Id==runner.BowSupportGuardId);
            var report=Roster.Reports.Find(r=>r.Id==runner.IncidentId);
            if(helper!=null && helper.IsAlive && helper.Duty==GuardDuty.Posted && helper.Interior==null && helper.Refuge==null &&
                report!=null && report.ThreatId!=null && helper.HasBow && helper.ArrowsRemaining>0)
            {
                var helperPoint=Roster.Position(helper.Id,now);
                var position=Local(actor.transform.position);
                double dx=helperPoint.X-position.X,dz=helperPoint.Z-position.Z;
                if(dx*dx+dz*dz<=16 && runner.Route.Count>=2)
                {
                    // Return to the witnessed firing position, not the enemy's unseen current position.
                    var route=geometry.RouteBetweenPoints(helperPoint,runner.Route[0]);
                    if(route.Count>=2)
                        Roster.DispatchDefense("archer:"+runner.IncidentId,report.ThreatId,now,
                            new Dictionary<string,List<SavedRoutePoint>>{{helper.Id,route}},1,2.8,DaggerfallUnity.Instance.WorldTime.TimeScale);
                }
            }
            var home=geometry.RouteBetweenPoints(Local(actor.transform.position),runner.Post);
            if(home.Count>=2)
            {
                Roster.Engage(runner.Id,now);
                Roster.ObserveEngagement(runner.Id,Local(actor.transform.position),runner.HealthFraction,now,false);
                Roster.ReturnFromEngagement(runner.Id,now,home,2.8,DaggerfallUnity.Instance.WorldTime.TimeScale);
                runner.BowSupportGuardId=null;
            }
            return true;
        }
        private void RecruitSeenDefenseGuards(GuardRecord runner,GuardActor caller,double now)
        {
            var report=Roster.Reports.Find(r=>r.Id==runner.IncidentId);
            if(report==null || report.ThreatId==null || runner.Route.Count<2) return;
            // Only this actual messenger recruits; newly recruited guards do not
            // broadcast the report across the settlement in the same update.
            foreach(var candidate in new List<GuardActor>(actors.Values))
            {
                if(!candidate || !candidate.gameObject.activeInHierarchy || candidate==caller ||
                    candidate.GuardId==runner.BowSupportGuardId) continue;
                var guard=Roster.Guards.Find(g=>g.Id==candidate.GuardId);
                if(guard==null || !guard.IsAlive || guard.Duty!=GuardDuty.Posted || guard.Interior!=null || guard.Refuge!=null ||
                    !CivilianResponses.Sees(caller.transform.position,candidate.transform.position,20)) continue;
                var route=geometry.RouteBetweenPoints(Roster.Position(guard.Id,now),runner.Route[0]);
                if(route.Count<2) continue;
                var sent=Roster.DispatchDefense("support:"+runner.IncidentId+":"+guard.Id,report.ThreatId,now,
                    new Dictionary<string,List<SavedRoutePoint>>{{guard.Id,route}},1,2.8,DaggerfallUnity.Instance.WorldTime.TimeScale);
                if(sent.Count>0) Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DEFENSE_SUPPORT_RECRUIT: caller="+runner.Id+"; guard="+guard.Id);
            }
        }
    }
}
