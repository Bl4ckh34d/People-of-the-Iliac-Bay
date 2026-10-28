using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private System.Action<System.Exception> BeginStationaryHealthHit(Transform target)
        {
            var npc=target?target.GetComponent<StaticNPC>():null;var host=GetComponent<PeopleOfTheIliacBayMod>();
            var life=npc && host?host.EnsureStationaryLife(npc):null;return life?life.BeginPlayerHit(false):null;
        }
        internal bool OwnsStationaryCorpses(WorldStateStore world,int map)
        {return Ready && worldStore==world && population!=null && population.SettlementId==map && !GameManager.Instance.PlayerEnterExit.IsPlayerInsideDungeon;}
        internal void ObserveStationaryPlayerHit(WorldStateStore world,NativePerson person,Vector3 position)
        {
            if(!OwnsStationaryCorpses(world,person.SettlementId) || !ReactionsEnabled)return;
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();var alarm=world.RecordViolence(person.SettlementId,person.Id,now);
            if(!alarm.HasLocation)
            {
                var root=interior?interior.transform:directory.transform;var point=root.InverseTransformPoint(position-Vector3.up);
                world.LocateViolence(alarm.Id,interior?interior.EntryDoor.buildingKey:-1,point.x,point.y,point.z);
            }
            foreach(var actor in actors.Values)
                if(actor && actor.gameObject.activeInHierarchy && actor.Resident.IsAlive && actor.Resident.Id!=person.Id)
                {
                    var witness=CivilianResponses.Observe(world,alarm,actor.Resident.Id,actor.transform,position,actor.IsSleeping,now,interior?interior.EntryDoor.buildingKey:-1);
                    if(witness!=null && !witness.HasPosition)
                    {
                        var ground=actor.transform.parent.InverseTransformPoint(actor.GroundPosition);witness.HasPosition=true;
                        witness.BuildingKey=interior?interior.EntryDoor.buildingKey:-1;witness.X=ground.x;witness.Y=ground.y;witness.Z=ground.z;
                    }
                }
            ObserveKeeperViolence(alarm,position,now);PrepareIndoorEscapes(now);
        }
        internal bool ReportStationaryPlayerDeath(WorldStateStore world,CivilianIncident alarm)
        {
            if(!OwnsStationaryCorpses(world,alarm.SettlementId))return false;
            double now=DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();SpreadInnAlarm(alarm,now);
            var player=GameManager.Instance.PlayerEntity;var guards=GetComponent<GuardPopulation>();player.TallyCrimeGuildRequirements(false,5);
            if(guards && guards.OwnsSettlement)
            {
                if(!alarm.People.Exists(person=>person.DirectWitness))world.TryRegisterNativeUnwitnessedScene(alarm.Id);
                else if(interior){player.CrimeCommitted=PlayerEntity.Crimes.Murder;world.MarkMurderAccusationIssued(alarm.Id);}
                var local=new RoutePoint(alarm.Corpse.X,alarm.Corpse.Z);
                guards.ReportCivilianCrime(alarm,interior?interior.transform.TransformPoint(new Vector3((float)alarm.Corpse.X,(float)alarm.Corpse.Y,(float)alarm.Corpse.Z)):geometry.ScenePoint(local));
            }
            else {player.CrimeCommitted=PlayerEntity.Crimes.Murder;player.SpawnCityGuards(true);}
            return true;
        }
    }
}
