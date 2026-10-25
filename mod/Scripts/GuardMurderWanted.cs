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
        // Reacquisition reads only a loaded, visible body. Receipt contains no position.
        private bool TryResumeKnownMurderer(GuardRecord guard,GuardActor actor,double now,bool routeTick)
        {
            if(!routeTick || !EnemyDefense || worldStore==null || guard.Refuge!=null || guard.Interior!=null ||
                guard.Duty!=GuardDuty.Posted || !actor || !actor.gameObject.activeInHierarchy)return false;
            foreach(var target in UnityEngine.Object.FindObjectsOfType<DaggerfallEntityBehaviour>())
            {
                if(!IsReacquirableMurderer(target) || target.Entity.IsInvisible || target.GetComponent<GuardActor>())continue;
                string identity=ThreatKey(target);
                if(!worldStore.GuardKnowsHostileMurder(directory.MapID,guard.Id,identity) ||
                    !CivilianResponses.Sees(actor.transform.position,target.transform.position,20))continue;
                var route=geometry.RouteBetweenPoints(Roster.Position(guard.Id,now),Local(target.transform.position));
                if(route.Count<2)continue;
                string incident="murder-reacquired:"+guard.Id+":"+now.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
                if(Roster.DispatchDefense(incident,identity,now,new Dictionary<string,List<SavedRoutePoint>> {
                    {guard.Id,route} },1,2.8,DaggerfallUnity.Instance.WorldTime.TimeScale).Count==0)continue;
                defenseEnemies[identity]=target;
                actor.BeginDefenseCombat(target,now);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_MURDER_REACQUIRED: guard="+guard.Id+"; target="+identity);
                return true;
            }
            return false;
        }
        private bool IsKnownMurdererDefense(GuardRecord guard,GuardReport report,DaggerfallEntityBehaviour target)
        {
            return report!=null && report.ThreatId!=null && target && target.Entity!=null && target.Entity.CurrentHealth>0 &&
                !target.Entity.IsInvisible && IsReacquirableMurderer(target) && worldStore!=null &&
                worldStore.GuardKnowsHostileMurder(directory.MapID,guard.Id,report.ThreatId) && ThreatKey(target)==report.ThreatId;
        }
        private static bool IsReacquirableMurderer(DaggerfallEntityBehaviour target)
        {
            if(!target || !target.gameObject.activeInHierarchy || target.Entity==null || target.Entity.CurrentHealth<=0 ||
                target.Entity.Team==MobileTeams.PlayerAlly)return false;
            if(target.Entity is EnemyEntity)return true;
            var resident=target.GetComponent<ResidentActor>();
            return resident!=null && resident.Resident!=null && resident.Resident.IsAlive &&
                !resident.Resident.IsQuestProtected && target.Entity is CivilianEntity;
        }
    }
}
