using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class GuardPopulation
    {
        private bool UpdateCorpseSuspectSearch(GuardRecord guard,GuardActor actor,double now,bool routeTick)
        {
            var report=Roster.Reports.Find(r=>r.Id==guard.IncidentId && r.CorpseSuspect!=null);
            if(report==null || guard.Duty==GuardDuty.Returning)return false;
            // A physical assault on this guard has its own native player evidence.
            if(guard.Duty==GuardDuty.Engaged && !actor.DefenseTarget &&
                actor.GetComponent<EnemyMotor>().IsHostile && report.CorpseSuspect.Kind!=CorpseSuspectKind.Player)return false;
            var alarm=worldStore.OutdoorCorpses(directory.MapID).Find(a=>"outdoor:"+a.Id==report.Id);
            if(guard.Duty==GuardDuty.Engaged)actor.Capture(now);else actor.Place(now);
            if(alarm==null || alarm.CrimeResolved || Roster.CorpseResponseExpired(guard.Id,now))
            {
                actor.EndCombat();
                if(routeTick && alarm!=null && alarm.CrimeResolved && TryMurdererCorpseCleanup(guard,actor,report,now))return true;
                if(routeTick && (guard.Duty==GuardDuty.Engaged || alarm!=null && alarm.CrimeResolved))
                {
                    if(guard.Duty!=GuardDuty.Engaged)Roster.Engage(guard.Id,now);
                    var route=geometry.RouteBetweenPoints(Local(actor.transform.position),guard.Post);
                    if(route.Count>=2)Roster.ReturnFromEngagement(guard.Id,now,route,2.8,DaggerfallUnity.Instance.WorldTime.TimeScale);
                }
                return true;
            }
            var known=report.CorpseSuspect;DaggerfallEntityBehaviour target=null;
            if(known.Kind==CorpseSuspectKind.Player)target=GameManager.Instance.PlayerEntityBehaviour;
            else defenseEnemies.TryGetValue(known.Identity,out target);
            bool valid=target && target.Entity!=null && target.Entity.CurrentHealth>0 &&
                !target.Entity.IsInvisible && target.gameObject.activeInHierarchy;
            bool sees=valid && CivilianResponses.Sees(actor.transform.position,target.transform.position,20);
            if(sees && now<report.InvestigationUntil)
            {
                var point=Local(target.transform.position);
                sees=worldStore.TryObserveCorpseSuspect(alarm.Id,guard.Id,known.Kind,known.Identity,point.X,point.Z,
                    Vector3.Distance(actor.transform.position,target.transform.position),true,now);
            }
            if(sees)
            {
                if(known.Kind==CorpseSuspectKind.Player)
                {
                    if(!alarm.AccusationIssued)worldStore.MarkMurderAccusationIssued(alarm.Id);
                    GameManager.Instance.PlayerEntity.CrimeCommitted=PlayerEntity.Crimes.Murder;
                    actor.BeginCombat(now);
                }
                else actor.BeginDefenseCombat(target,now);
            }
            // Existing native combat follows its own last seen position. A fresh
            // searcher keeps its physical patrol until it personally sees the target.
            return true;
        }
    }
}
