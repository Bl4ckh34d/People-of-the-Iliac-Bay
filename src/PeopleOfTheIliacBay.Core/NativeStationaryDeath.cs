using System;

namespace PeopleOfTheIliacBay.Core
{
    public sealed partial class WorldStateStore
    {
        public void EnsureNativeLocationIdentity(int locationId)
        {
            Require(locationId>=0,"Native person needs an actual location identity.");
            if(CurrentPopulation(locationId)==null)state.Settlements.Add(new SettlementPopulation{SettlementId=locationId,IsPartial=true});
        }
        public bool TryRegisterNativeUnwitnessedScene(string incidentId)
        {
            var alarm=FindIncident(incidentId);var victim=AlarmResident(alarm.SettlementId,alarm.VictimId);
            if(victim.IsAlive || !alarm.HasLocation || alarm.BuildingKey<0 ||
                !(VictimOwnsScene(alarm,victim,state) || alarm.People.Exists(person=>person.HeardViolence)) ||
                !alarm.People.TrueForAll(person=>person.HeardViolence && !person.DirectWitness))return false;
            RegisterUnwitnessedScene(incidentId);return true;
        }
        // Called for an actual native death event. The native entity may already
        // have marked its shared household alias dead before this subscriber runs.
        public CivilianIncident RecordNativeStationaryDeath(string personId,double now,int building,double x,double y,double z,bool playerHit)
        {
            var person=IndexedNativePerson(personId);
            Require(person!=null && ValidTime(now) && building>=-1 && ValidCoordinate(x) && ValidCoordinate(y) && ValidCoordinate(z),
                "Native stationary death needs its exact identity, time and position.");
            var alarm=state.AlarmIncidents.Find(value=>value.VictimId==personId);
            if(alarm==null)
            {
                Require(state.NextIncidentSerial>0 && state.NextIncidentSerial<long.MaxValue-1,"Incident generation serial exhausted.");
                alarm=new CivilianIncident {Id="murder:"+personId,VictimId=personId,SettlementId=person.SettlementId,
                    StartedAt=now,ExpiresAt=now+900,GenerationSerial=state.NextIncidentSerial++};
                state.AlarmIncidents.Add(alarm);
            }
            if(!alarm.HasLocation)LocateViolence(alarm.Id,building,x,y,z);
            person.IsAlive=false;var member=ResolveResident(personId);if(member!=null)member.IsAlive=false;
            ConfirmCorpse(alarm.Id,now,building,x,y,z);
            if(playerHit)MarkPlayerMurder(alarm.Id);
            return alarm;
        }
    }
}
