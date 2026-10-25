using System;
using System.Collections.Generic;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.MagicAndEffects;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private sealed class HomeSleepClock
        {
            internal ResidentActor Actor;
            internal double CheckedAt,Seconds;
            internal int Health,Magicka;
            internal bool AtHomeAsleep,Eligible=true;
            internal float NextCheck;
        }
        private readonly Dictionary<string,HomeSleepClock> homeSleepClocks=new Dictionary<string,HomeSleepClock>();
        private object homeSleepEpoch;

        private void ObserveHomeSleep(Plan plan,ResidentActor actor,double now,bool flush=false)
        {
            if(worldStore==null || !actor || !actor.gameObject.activeInHierarchy && !flush || plan==null)return;
            var binding=actor.GetComponent<NativeNpcVitalsBinding>();
            int hp,maxHp,mp,maxMp;
            if(!binding || !binding.TryReadCurrent(out hp,out maxHp,out mp,out maxMp))return;
            string id=plan.Resident.Id;
            if(hp>=maxHp) {homeSleepClocks.Remove(id);return;}
            object epoch=worldStore.CaptureNpcVitalsWriteEpoch();
            if(!ReferenceEquals(epoch,homeSleepEpoch)) {homeSleepClocks.Clear();homeSleepEpoch=epoch;}
            HomeSleepClock clock;
            bool first=!homeSleepClocks.TryGetValue(id,out clock);
            bool rebound=!first && clock.Actor!=actor;
            if(!first && !rebound && !flush && Time.unscaledTime<clock.NextCheck)return;
            var saved=worldStore.ReadNpcProgress(id);
            if(saved==null || !saved.VitalsInitialized || saved.CurrentHealth!=hp || saved.CurrentMagicka!=mp)
            {binding.Capture();saved=worldStore.ReadNpcProgress(id);first=true;}
            if(saved==null || !saved.VitalsInitialized || saved.CurrentHealth!=hp || saved.CurrentMagicka!=mp)return;
            if(first)
            {
                clock=new HomeSleepClock {Actor=actor,CheckedAt=Math.Max(0,Math.Min(now,saved.VitalsCapturedAt)),Health=hp,Magicka=mp};
                homeSleepClocks[id]=clock;
            }
            clock.Actor=actor;
            clock.NextCheck=Time.unscaledTime+0.5f;
            if(clock.Health!=hp || clock.Magicka!=mp || now<clock.CheckedAt)
            {clock.CheckedAt=now;clock.Seconds=0;clock.AtHomeAsleep=false;}
            clock.Health=hp;clock.Magicka=mp;
            var effects=actor.GetComponent<EntityEffectManager>();
            var home=population.Households.Find(h=>h.BuildingKey==plan.Resident.HomeKey && h.Residents.Exists(r=>r.Id==id));
            bool ownRoom=interior && interior.EntryDoor.buildingKey==plan.Resident.HomeKey;
            bool knownBed=home!=null && (home.BedCapacity>0 || ownRoom && indoorBeds.Count>0);
            bool available=knownBed && plan.Resident.IsAlive && !plan.Resident.IsQuestProtected && !QuestOwned(plan.Resident) &&
                !actor.IsConversing && !IsAlarmed(id) &&
                !GameManager.Instance.PlayerEnterExit.IsPlayerInsideDungeon && effects && effects.PoisonCount==0 && effects.DiseaseCount==0 &&
                worldStore.ActiveNpcTask(id)==null && worldStore.ActiveNpcAction(id)==null &&
                !worldStore.HasNpcTreatmentVisit(id) && !worldStore.HasCurrentNpcServiceDuty(id,now);
            double from=Math.Max(clock.CheckedAt,now-86400);
            if(available && now>from && worldStore.IsResidentAwayDuring(id,from,now))available=false;
            var visit=available && now>from?worldStore.GetObservedVisit(id):null;
            if(visit!=null && visit.BuildingKey!=plan.Resident.HomeKey && visit.AffectsRoutineDuring(from,now))available=false;
            if(available && now>from)
                foreach(var appointment in worldStore.AppointmentsFor(id))
                    if((appointment.Status=="accepted" || appointment.Status=="waiting" || appointment.Status=="attended") &&
                        appointment.StartsAt<now && AppointmentTiming.PlannedLeaveAt(appointment)>from) {available=false;break;}
            if(!available)
            {clock.CheckedAt=now;clock.Seconds=0;clock.AtHomeAsleep=false;clock.Eligible=false;return;}
            // A newly materialized known resident can finish its ordinary night
            // analytically. Loaded bodies require the preceding observation at home.
            if(first || rebound && clock.Eligible || clock.AtHomeAsleep)clock.Seconds+=ActiveSchedule(plan).HomeSleepSeconds(from,now);
            clock.Eligible=true;
            var state=Scheduled(plan,now);
            clock.AtHomeAsleep=ownRoom && state.Activity==Activity.Sleep && state.BuildingKey==plan.Resident.HomeKey;
            clock.CheckedAt=now;
            int hours=(int)Math.Floor(clock.Seconds/3600);
            if(hours<1)return;
            var body=actor.GetComponent<DaggerfallEntityBehaviour>();
            if(!body || body.Entity==null)return;
            int rate=NativeNpcRestTreatment.RecoveryRatePerGameHour(body.Entity);
            int after=(int)Math.Min(maxHp,(long)hp+(long)rate*hours);
            if(after<=hp)return;
            bool quiesced=body.Entity.Quiesce;
            try {body.Entity.Quiesce=true;body.Entity.SetHealth(after);binding.Capture();}
            finally {body.Entity.Quiesce=quiesced;}
            clock.Health=body.Entity.CurrentHealth;clock.Seconds-=hours*3600;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_HOME_SLEEP_HEAL: resident="+id+"; hours="+hours+"; rate="+rate+
                "; health="+hp+"/"+maxHp+"->"+body.Entity.CurrentHealth+"/"+maxHp+"; knownNight="+first);
        }

        private void FlushHomeSleep()
        {
            if(worldStore==null || population==null)return;
            double now=DaggerfallWorkshop.DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
            foreach(var plan in plans)
            {ResidentActor actor;if(actors.TryGetValue(plan.Resident.Id,out actor))ObserveHomeSleep(plan,actor,now,true);}
        }
    }
}
