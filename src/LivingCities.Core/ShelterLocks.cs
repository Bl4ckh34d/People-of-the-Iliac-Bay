using System;
using System.Collections.Generic;
namespace LivingCities.Core
{
    [Serializable]
    public sealed class ShelterLock
    {
        public int SettlementId,BuildingKey;
        public double StartedAt,Until;
    }
    public sealed partial class WorldStateStore
    {
        public void RecordShelterArrival(string incidentId,string residentId,double now)
        {
            var alarm=FindIncident(incidentId); var person=alarm.People.Find(p=>p.ResidentId==residentId);
            Require(ValidTime(now) && now>=alarm.StartedAt && person!=null && person.HasPosition &&
                person.ReachedRefuge && person.BuildingKey>=0 && person.Reaction==CivilianReaction.Flee &&
                AlarmResident(alarm.SettlementId,residentId).IsAlive,"Shelter lock requires a living fleeing arrival.");
            if(person.RefugeSealed) return;
            Require(CivilianAlarms.PersonActive(alarm,person,now) && ValidTime(now+3600),"Shelter arrival alarm has expired.");
            state.ShelterLocks.RemoveAll(s=>s.Until<=now);
            var seal=state.ShelterLocks.Find(s=>s.SettlementId==alarm.SettlementId && s.BuildingKey==person.BuildingKey);
            if(seal==null)
            {seal=new ShelterLock {SettlementId=alarm.SettlementId,BuildingKey=person.BuildingKey,StartedAt=now};state.ShelterLocks.Add(seal);}
            seal.Until=Math.Max(seal.Until,now+3600);person.RefugeSealed=true;
        }
        public bool ShelterLocked(int settlement,int building,double now)
        {
            Require(ValidTime(now),"Invalid shelter lock time.");
            return state.ShelterLocks.Exists(s=>s.SettlementId==settlement && s.BuildingKey==building && now>=s.StartedAt && now<s.Until);
        }
        private static List<ShelterLock> CopyShelterLocks(WorldState source)
        {
            var result=new List<ShelterLock>();var seen=new HashSet<string>();
            foreach(var seal in source.ShelterLocks ?? new List<ShelterLock>())
            {
                Require(seal!=null && seal.BuildingKey>=0 && source.Settlements.Exists(s=>s.SettlementId==seal.SettlementId) &&
                    ValidTime(seal.StartedAt) && ValidTime(seal.Until) && seal.Until>=seal.StartedAt+3600 &&
                    seen.Add(seal.SettlementId+":"+seal.BuildingKey),"Invalid or duplicate shelter lock.");
                result.Add(new ShelterLock {SettlementId=seal.SettlementId,BuildingKey=seal.BuildingKey,StartedAt=seal.StartedAt,Until=seal.Until});
            }
            return result;
        }
    }
}
