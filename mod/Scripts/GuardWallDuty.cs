using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class GuardPopulation
    {
        private bool WallAvailable(int building,string guardId,int? bedRecord=null)
        {
            return !Roster.Guards.Exists(g=>g.Id!=guardId && g.IsAlive &&
                (g.PostRecall!=null && g.PostRecall.WallDuty!=null && g.PostRecall.WallDuty.ConflictsWith(building,bedRecord) ||
                 g.Refuge!=null && g.Refuge.ConflictsWith(building,bedRecord)));
        }
        private void ArmGateRecall(GuardRecord guard,GateDutyPair pair,double now)
        {
            var route=pair.ReturnRoute;if(route==null)return;
            double dusk=Math.Floor(now/86400)*86400+DaggerfallDateTime.DuskHour*3600;
            double departure=dusk-RouteLength(route)*DaggerfallUnity.Instance.WorldTime.TimeScale/1.3-60;
            if(AtPoint(guard.Post,pair.Outside) && guard.PostRecall==null && guard.IsAlive && now<departure &&
                guard.ArrivesAt<=departure && guard.Interior==null && guard.Refuge==null &&
                (guard.Duty==GuardDuty.Posted || guard.Duty==GuardDuty.Returning &&
                    guard.IncidentId!=null && guard.IncidentId.StartsWith("post:",StringComparison.Ordinal)))
                Roster.SchedulePostRecall(guard.Id,route,departure,dusk,1.3,DaggerfallUnity.Instance.WorldTime.TimeScale);
            // At shift end the outgoing guard physically leaves inner staging for
            // its own relief post, then uses the ordinary bed/refuge planner.
            // A gate-bed shortcut would reserve staging throughout the night.
        }
    }
}
