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
        public void HearGuardHalt(GuardActor caller,DaggerfallEntityBehaviour target)
        {
            if(!OwnsExterior||!caller||!target||GameManager.IsGamePaused)return;
            bool player=target==GameManager.Instance.PlayerEntityBehaviour;
            if(player&&GameManager.Instance.PlayerEntity.CrimeCommitted==PlayerEntity.Crimes.None)return;
            var source=Roster.Guards.Find(guard=>guard.Id==caller.GuardId);
            if(source!=null && Roster.Reports.Exists(report=>report.Id==source.IncidentId && report.HasCorpseInquiryWindow))
            {
                // Hearing a lawful Halt does not create another unbounded report.
                RecruitNearbyCorpseSearchGuards(Now(),caller);
                return;
            }
            string threat=player?null:RegisterHostileThreat(target);
            if(!player&&threat==null)return;
            var destination=Local(target.transform.position);double now=Now();
            var routes=new Dictionary<string,List<SavedRoutePoint>>();
            foreach(var guard in Roster.Guards)
            {
                if(guard.Id==caller.GuardId||!guard.IsAlive||guard.Duty!=GuardDuty.Posted||guard.Interior!=null||guard.Refuge!=null)continue;
                var from=Roster.Position(guard.Id,now);var scene=geometry.ScenePoint(new RoutePoint(from.X,from.Z));
                if((scene-caller.transform.position).sqrMagnitude>100*100)continue;
                var route=geometry.RouteBetweenPoints(from,destination);if(route.Count>=2)routes.Add(guard.Id,route);
            }
            if(routes.Count==0)return;
            string id="halt:"+caller.GuardId+":"+now.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
            if(player)Roster.Dispatch(id,now,routes,Math.Min(256,routes.Count),2.8,DaggerfallUnity.Instance.WorldTime.TimeScale);
            else Roster.DispatchDefense(id,threat,now,routes,Math.Min(256,routes.Count),2.8,DaggerfallUnity.Instance.WorldTime.TimeScale);
            // Hearing starts a route to the sighting. It never grants sight through buildings.
        }
    }
}
