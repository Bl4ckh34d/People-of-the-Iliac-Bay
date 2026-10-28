using System;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.MagicAndEffects;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private void ObserveTravelerSleep(TravelerPlan plan,ResidentActor actor,TravelerSnapshot state,
            AlarmPerson reaction,double now)
        {
            if(worldStore==null || plan==null || plan.Person==null)return;
            string id=plan.Person.Id;
            object epoch=worldStore.CaptureNpcVitalsWriteEpoch();
            if(!ReferenceEquals(epoch,homeSleepEpoch)) {homeSleepClocks.Clear();homeSleepEpoch=epoch;}
            if(!actor || !actor.gameObject.activeInHierarchy || !plan.Person.IsAlive)
            {homeSleepClocks.Remove(id);return;}
            var binding=actor.GetComponent<NativeNpcVitalsBinding>();
            var body=actor.GetComponent<DaggerfallEntityBehaviour>();
            int hp,maxHp,mp,maxMp;
            if(!binding || !body || body.Entity==null || !binding.TryReadCurrent(out hp,out maxHp,out mp,out maxMp) ||
                body.Entity.CurrentHealth!=hp || body.Entity.MaxHealth!=maxHp ||
                body.Entity.CurrentMagicka!=mp || body.Entity.MaxMagicka!=maxMp)return;
            if(hp>=maxHp && mp>=maxMp) {homeSleepClocks.Remove(id);return;}
            HomeSleepClock clock;
            bool first=!homeSleepClocks.TryGetValue(id,out clock) || clock.Actor!=actor;
            if(!first && Time.unscaledTime<clock.NextCheck)return;
            var effects=actor.GetComponent<EntityEffectManager>();
            bool available=interior && plan.RoomInterior==interior && actor.transform.parent==interior.transform &&
                plan.Bed!=null && plan.RoomFollower!=null && plan.RoomFollower.Arrived && !plan.RoomFollower.Leaving &&
                plan.Visit.Admission==TravelerAdmission.Admitted && now>=plan.Journey.CheckInAt && now<plan.Journey.CheckOutAt &&
                worldStore.TravelerBed(plan.Visit.Id,now)=="bed:"+plan.Bed.RecordIndex &&
                state.Phase==TravelerPhase.Sleeping && !actor.IsConversing &&
                reaction==null && !IsAlarmed(id) && !QuestOwned(plan.Resident) &&
                !GameManager.Instance.PlayerEnterExit.IsPlayerInsideDungeon && effects &&
                effects.PoisonCount==0 && effects.DiseaseCount==0 && worldStore.ActiveNpcTask(id)==null &&
                worldStore.ActiveNpcAction(id)==null && !worldStore.HasNpcTreatmentVisit(id) &&
                !worldStore.HasCurrentNpcServiceDuty(id,now);
            if(first)
            {
                // A new/rebuilt body starts observing now. A stored timestamp never grants unseen Inn rest.
                clock=new HomeSleepClock {Actor=actor,CheckedAt=now,Health=hp,Magicka=mp};
                homeSleepClocks[id]=clock;
            }
            int hours=AccumulateTravelerSleep(clock,plan.Journey,now,hp,mp,available);
            clock.Actor=actor;clock.NextCheck=Time.unscaledTime+0.5f;
            if(hours<1)return;
            int healthRate=NativeNpcRestTreatment.RecoveryRatePerGameHour(body.Entity);
            int magickaRate=TravelerMagickaRecoveryPerGameHour(maxMp,
                body.Entity.Career!=null && body.Entity.Career.NoRegenSpellPoints);
            int afterHp=(int)Math.Min(maxHp,(long)hp+(long)healthRate*hours);
            int afterMp=(int)Math.Min(maxMp,(long)mp+(long)magickaRate*hours);
            if(afterHp<=hp && afterMp<=mp)return;
            bool quiesced=body.Entity.Quiesce;
            try
            {
                body.Entity.Quiesce=true;
                if(afterHp>hp)body.Entity.SetHealth(afterHp);
                if(afterMp>mp)body.Entity.SetMagicka(afterMp);
                binding.Capture();
            }
            finally {body.Entity.Quiesce=quiesced;}
            clock.Health=body.Entity.CurrentHealth;clock.Magicka=body.Entity.CurrentMagicka;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TRAVELER_SLEEP_RECOVERY: traveler="+id+"; hours="+hours+
                "; health="+hp+"/"+maxHp+"->"+clock.Health+"/"+maxHp+
                "; magicka="+mp+"/"+maxMp+"->"+clock.Magicka+"/"+maxMp+"; observedBookedBed=true");
        }

        private static int AccumulateTravelerSleep(HomeSleepClock clock,TravelerJourney journey,double now,
            int health,int magicka,bool available)
        {
            bool unchanged=clock.Health==health && clock.Magicka==magicka && now>=clock.CheckedAt;
            if(!available || !unchanged || !clock.AtHomeAsleep)clock.Seconds=0;
            else
            {
                double elapsed=ObservedTravelerSleepSeconds(journey,clock.CheckedAt,now);
                if(now>clock.CheckedAt && elapsed==0)clock.Seconds=0;
                else clock.Seconds+=elapsed;
            }
            clock.CheckedAt=now;clock.Health=health;clock.Magicka=magicka;
            clock.AtHomeAsleep=available;clock.Eligible=available;
            if(!available)return 0;
            int hours=(int)Math.Floor(clock.Seconds/3600);
            clock.Seconds-=hours*3600;
            return hours;
        }

        internal static int TravelerMagickaRecoveryPerGameHour(int maximumMagicka,bool noRegenSpellPoints)
        {
            // Native CalculateSpellPointRecoveryRate, using this actor's own capacity and career restriction.
            return maximumMagicka<1 || noRegenSpellPoints?0:Math.Max(maximumMagicka/8,1);
        }

        internal static double ObservedTravelerSleepSeconds(TravelerJourney journey,double from,double until)
        {
            if(journey==null || double.IsNaN(from) || double.IsInfinity(from) || double.IsNaN(until) ||
                double.IsInfinity(until) || from<0 || until<=from || until-from>86400 ||
                from<journey.CheckInAt || until>=journey.CheckOutAt ||
                journey.Evaluate(from).Phase!=TravelerPhase.Sleeping || journey.Evaluate(until).Phase!=TravelerPhase.Sleeping)return 0;
            // Existing traveler sleep boundaries are whole game minutes. Reject any intervening awake minute.
            for(double cursor=from;cursor<until;)
            {
                double next=Math.Min(until,(Math.Floor(cursor/60)+1)*60);
                if(journey.Evaluate(cursor+(next-cursor)/2).Phase!=TravelerPhase.Sleeping)return 0;
                cursor=next;
            }
            return until-from;
        }
    }
}
