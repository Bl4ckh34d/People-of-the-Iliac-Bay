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
        private bool TryResumeKnownPursuit(GuardRecord guard,GuardActor actor,double now)
        {
            var game=GameManager.Instance;
            if(guard.Refuge!=null || guard.Interior!=null ||
                (guard.Duty!=GuardDuty.Posted && guard.Duty!=GuardDuty.Returning) ||
                game.PlayerEntity.CrimeCommitted==PlayerEntity.Crimes.None || game.PlayerEntity.IsInvisible ||
                !Roster.KnowsPlayerCrime(guard.Id) ||
                !CivilianResponses.Sees(actor.transform.position,game.PlayerMotor.transform.position,20)) return false;
            if(guard.Duty==GuardDuty.Posted)
            {
                var route=geometry.RouteBetweenPoints(Roster.Position(guard.Id,now),Local(game.PlayerMotor.transform.position));
                if(route.Count<2) return false;
                string incident="reacquired:"+guard.Id+":"+now.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
                if(Roster.Dispatch(incident,now,new Dictionary<string,List<SavedRoutePoint>> {
                    {guard.Id,route} },1,2.8,DaggerfallUnity.Instance.WorldTime.TimeScale).Count==0) return false;
            }
            actor.BeginCombat(now);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PURSUIT_REACQUIRED: guard="+guard.Id);
            return true;
        }
        private void RecruitNearbyPursuitGuards(double now)
        {
            var game=GameManager.Instance;
            if(interior || game.PlayerEntity.CrimeCommitted==PlayerEntity.Crimes.None) return;
            // Snapshot callers so new recruits cannot cascade across the town
            // during the same update. All contacts use actual loaded actors.
            var callers=new List<GuardActor>();
            foreach(var actor in actors.Values)
                if(actor && actor.gameObject.activeInHierarchy && actor.CombatReady && !actor.DefenseTarget)
                    callers.Add(actor);
            foreach(var caller in callers)
            {
                var source=Roster.Guards.Find(g=>g.Id==caller.GuardId);
                if(source==null || source.LastSeenSuspect==null)continue;
                bool corpseInquiry=Roster.Reports.Exists(report=>report.Id==source.IncidentId && report.HasCorpseInquiryWindow);
                foreach(var candidate in actors.Values)
                {
                    if(!candidate || !candidate.gameObject.activeInHierarchy || candidate.CombatReady || candidate.DefenseTarget) continue;
                    float distance=Vector3.Distance(caller.transform.position,candidate.transform.position);
                    bool seesPursuit=!game.PlayerEntity.IsInvisible &&
                        CivilianResponses.Sees(candidate.transform.position,game.PlayerMotor.transform.position,20) &&
                        CivilianResponses.Sees(candidate.transform.position,caller.transform.position,40);
                    bool contact=!corpseInquiry && distance<=12 &&
                        CivilianResponses.Sees(caller.transform.position,candidate.transform.position,12);
                    if((!seesPursuit && !contact) ||
                        !Roster.CanRecruitPursuit(caller.GuardId,candidate.GuardId,now,distance,seesPursuit))continue;
                    var sighting=seesPursuit?Local(game.PlayerMotor.transform.position):source.LastSeenSuspect;
                    var route=geometry.RouteBetweenPoints(Roster.Position(candidate.GuardId,now),sighting);
                    if(route.Count<2) continue;
                    string incident="pursuit:"+candidate.GuardId+":"+now.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
                    var sent=Roster.Dispatch(incident,now,new Dictionary<string,List<SavedRoutePoint>> {
                        {candidate.GuardId,route} },1,2.8,DaggerfallUnity.Instance.WorldTime.TimeScale);
                    if(sent.Count==0) continue;
                    candidate.BeginCombat(now,sighting);
                    // Passing the alarm along must not renew stale knowledge.
                    sent[0].SearchUntil=Math.Min(sent[0].SearchUntil,source.SearchUntil);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_PURSUIT_RECRUIT: caller="+caller.GuardId+"; guard="+candidate.GuardId+"; metres="+distance);
                }
            }
        }
    }
}
