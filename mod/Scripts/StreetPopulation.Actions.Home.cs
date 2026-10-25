using System;
using System.Collections.Generic;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private bool AwakeHomeActionFitsWindow(Plan plan,ResidentActor actor,NpcActionJob job,double now)
        {
            if(plan==null || !actor || job==null || job.DeadlineAt<now || !interior ||
                plan.Resident.HomeKey!=interior.EntryDoor.buildingKey) return false;
            var current=Scheduled(plan,now);
            var window=new List<Activity>();
            // Local actions last at most fifteen minutes. Sample every minute so
            // a brief work/sleep boundary cannot hide behind a safe deadline.
            for(double sample=now;sample<job.DeadlineAt;)
            {
                window.Add(Scheduled(plan,sample).Activity);
                double next=(Math.Floor(sample/60d)+1d)*60d;
                if(next<=sample) next=sample+1d;
                sample=Math.Min(job.DeadlineAt,next);
            }
            window.Add(Scheduled(plan,job.DeadlineAt).Activity);
            if(window.Count==0)window.Add(current.Activity);
            return NpcHomeActionPolicy.Allows(job.Kind,true,!actor.IsSleeping,current.Activity,window,out _);
        }
    }
}
