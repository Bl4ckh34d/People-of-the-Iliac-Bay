using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class GuardPopulation
    {
        private int corpseRecruitmentCandidate;
        private int corpseRecruitmentFrame=-1,corpseRecruitmentSurveys;

        private bool RecruitNearbyCorpseSearchGuards(double now,GuardActor onlyCaller=null)
        {
            if(!OwnsExterior || Roster==null || geometry==null)return false;
            if(corpseRecruitmentFrame!=Time.frameCount)
            {corpseRecruitmentFrame=Time.frameCount;corpseRecruitmentSurveys=0;}
            if(corpseRecruitmentSurveys>=2)return false;
            Roster.Advance(now);
            var candidates=new List<GuardActor>();var callers=new List<GuardActor>();
            foreach(var actor in actors.Values)
            {
                if(!actor || !actor.gameObject.activeInHierarchy)continue;
                candidates.Add(actor);
                if(onlyCaller && actor!=onlyCaller)continue;
                var source=Roster.Guards.Find(row=>row.Id==actor.GuardId);
                if(source!=null && Roster.Reports.Exists(report=>report.Id==source.IncidentId &&
                    report.HasCorpseInquiryWindow && report.InvestigationUntil>now))callers.Add(actor);
            }
            candidates.Sort((a,b)=>string.CompareOrdinal(a.GuardId,b.GuardId));
            callers.Sort((a,b)=>string.CompareOrdinal(a.GuardId,b.GuardId));
            if(candidates.Count==0)return false;
            int start=corpseRecruitmentCandidate%candidates.Count;
            foreach(var caller in callers)
                for(int i=0;i<candidates.Count;i++)
                {
                    int index=(start+i)%candidates.Count;var candidate=candidates[index];
                    float distance=Vector3.Distance(caller.transform.position,candidate.transform.position);
                    if(!Roster.CanRecruitCorpseSearchGuard(caller.GuardId,candidate.GuardId,now,distance,true) ||
                        !CivilianResponses.Sees(caller.transform.position,candidate.transform.position,
                            (float)GuardRoster.CorpseSearchContactRadius))continue;
                    corpseRecruitmentCandidate=(index+1)%candidates.Count;
                    var source=Roster.Guards.Find(row=>row.Id==caller.GuardId);
                    var report=Roster.Reports.Find(row=>row.Id==source.IncidentId);
                    var from=Roster.Position(candidate.GuardId,now);var contact=Roster.Position(caller.GuardId,now);
                    corpseRecruitmentSurveys++;
                    var join=geometry.RouteBetweenPoints(from,contact);
                    if(join==null || join.Count<2)return false;
                    if(corpseRecruitmentSurveys>=2)return false;
                    corpseRecruitmentSurveys++;
                    var entry=geometry.RouteBetweenPoints(contact,report.Entrance);
                    GuardRecord recruited;
                    bool accepted=entry!=null && entry.Count>=2 && Roster.TryRecruitCorpseSearchGuard(
                        caller.GuardId,candidate.GuardId,now,distance,true,join,entry,2.8,
                        DaggerfallUnity.Instance.WorldTime.TimeScale,out recruited);
                    if(accepted)Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CORPSE_SEARCH_RECRUIT: case="+report.Id+
                        "; caller="+caller.GuardId+"; guard="+candidate.GuardId+"; total="+report.GuardIds.Count+
                        "; until="+report.InvestigationUntil+"; combatStarted=false");
                    // At most two route surveys and one new member per contact tick.
                    // New members can relay on a later tick, never in this snapshot.
                    return accepted;
                }
            return false;
        }
    }
}
