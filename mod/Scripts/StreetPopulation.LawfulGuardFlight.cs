using System;
using System.Collections.Generic;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Physical response to an observed lawful guard hit. A real refuge arrival
    // uses the existing defense ledger; it creates no crime against the guard.
    public sealed partial class StreetPopulation
    {
        private sealed class LawfulGuardFlight
        {
            public GuardActor Guard;
            public string GuardId;
            public AlarmPerson Reaction;
        }

        private readonly Dictionary<string,LawfulGuardFlight> lawfulGuardFlights=
            new Dictionary<string,LawfulGuardFlight>();

        public Action<Exception> BeginLawfulGuardResidentAttack(ResidentActor victim,DaggerfallEntityBehaviour source)
        {
            var body=victim ? victim.GetComponent<DaggerfallEntityBehaviour>() : null;
            if(!Ready || !ReactionsEnabled || !victim || !source || !body || body.Entity==null || source.Entity==null ||
                !victim.Resident.IsAlive || body.Entity.CurrentHealth<=0 || FindActor(victim.Resident.Id)!=victim)
                return null;
            var guard=source.GetComponent<GuardActor>();
            var victimScene=DefenseScene.For(victim);var guardScene=DefenseScene.For(source);
            if(!guard || !guard.gameObject.activeInHierarchy || victimScene==null || guardScene==null ||
                victimScene.Root!=guardScene.Root || !victimScene.PlayerPresent ||
                !CivilianResponses.Sees(source.transform.position,victim.transform.position,20))return null;
            int health=body.Entity.CurrentHealth;string residentId=victim.Resident.Id;string guardId=guard.GuardId;
            return error=>{
                if(error!=null || !victim || !source || !guard || guard.GuardId!=guardId ||
                    !victim.Resident.IsAlive || body.Entity==null || body.Entity.CurrentHealth>=health)return;
                BeginLawfulGuardFlight(victim,guard,guardId,residentId);
            };
        }

        private void BeginLawfulGuardFlight(ResidentActor victim,GuardActor guard,string guardId,string residentId)
        {
            var origin=victim.transform.parent;
            if(!origin)return;
            var local=origin.InverseTransformPoint(victim.transform.position-Vector3.up);
            lawfulGuardFlights[residentId]=new LawfulGuardFlight {Guard=guard,GuardId=guardId,
                Reaction=new AlarmPerson {ResidentId=residentId,DirectWitness=true,Reaction=CivilianReaction.Flee,
                    HasPosition=true,BuildingKey=interior ? interior.EntryDoor.buildingKey : -1,X=local.x,Y=local.y,Z=local.z}};
        }

        private AlarmPerson ActiveLawfulGuardFlight(string residentId)
        {
            var sheltered=worldStore==null || population==null ? null :
                worldStore.ResidentDefenseShelter(residentId,population.SettlementId);
            if(sheltered!=null)
            {
                double now=DaggerfallWorkshop.DaggerfallUnity.Instance.WorldTime.Now.ToSeconds();
                // Do not visibly replace a sheltered body with its schedule.
                // Offscreen release uses the ordinary existing routine projection.
                if(now>=sheltered.Until && (!interior || sheltered.Reaction.BuildingKey!=interior.EntryDoor.buildingKey))
                {worldStore.ReleaseDefenseShelter(residentId,now);lawfulGuardFlights.Remove(residentId);return null;}
                if(!sheltered.LandingObserved && interior && sheltered.Reaction.BuildingKey==interior.EntryDoor.buildingKey &&
                    BuildingActionDoorId(interior.EntryDoor)==sheltered.DoorId && indoorGeometry!=null && indoorGeometry.EntryGround.HasValue)
                {
                    var point=interior.transform.InverseTransformPoint(indoorGeometry.EntryGround.Value);
                    worldStore.ObserveDefenseShelterLanding(residentId,population.SettlementId,interior.EntryDoor.buildingKey,
                        BuildingActionDoorId(interior.EntryDoor),point.x,point.y,point.z,now);
                }
                return sheltered.Reaction;
            }
            ResidentActor actor=FindActor(residentId);Vector3 ignored;
            return actor && TryLawfulGuardFlightDanger(actor,out ignored) ? lawfulGuardFlights[residentId].Reaction : null;
        }

        private void RememberLawfulGuardShelterEntry(ResidentActor actor,StaticDoor door,double now)
        {
            LawfulGuardFlight flight;
            if(!lawfulGuardFlights.TryGetValue(actor.Resident.Id,out flight))return;
            worldStore.RecordDefenseShelter(actor.Resident.Id,population.SettlementId,BuildingActionDoorId(door),flight.Reaction,now);
        }

        private bool HasUnmappedDefenseShelter(string residentId)
        {
            var shelter=worldStore.ResidentDefenseShelter(residentId,population.SettlementId);
            return shelter!=null && !shelter.LandingObserved;
        }

        private bool HoldLawfulGuardShelter(ResidentActor actor,AlarmPerson reaction,bool hasKnownDanger)
        {
            if(!interior || !actor || hasKnownDanger)return false;
            var shelter=worldStore.ResidentDefenseShelter(actor.Resident.Id,population.SettlementId);
            if(shelter==null || !shelter.LandingObserved || !ReferenceEquals(shelter.Reaction,reaction) ||
                reaction.BuildingKey!=interior.EntryDoor.buildingKey)return false;
            var ground=interior.transform.TransformPoint(new Vector3((float)reaction.X,(float)reaction.Y,(float)reaction.Z));
            actor.Place(ground,actor.transform.forward,true);
            return true;
        }

        public bool TryGuardRefugeContext(string residentId,Func<int,string> buildingName,out string context)
        {
            context=null;
            if(!Ready || !interior || buildingName==null)return false;
            var actor=FindActor(residentId);
            var shelter=worldStore.ResidentDefenseShelter(residentId,population.SettlementId);
            if(!actor || !actor.gameObject.activeInHierarchy || actor.transform.parent!=interior.transform || shelter==null)return false;
            Vector3 ignored;var guards=GetComponent<GuardPopulation>();
            context=shelter.ActivityContext(interior.EntryDoor.buildingKey,buildingName(interior.EntryDoor.buildingKey),
                guards && guards.TryVisibleResidentPursuer(actor,out ignored));
            return context!=null;
        }

        private bool TryLawfulGuardFlightDanger(ResidentActor actor,out Vector3 danger)
        {
            danger=Vector3.zero;
            if(!actor || actor.Resident==null)return false;
            if(worldStore.ResidentDefenseShelter(actor.Resident.Id,population.SettlementId)!=null)
            {
                var guards=GetComponent<GuardPopulation>();
                return guards && guards.TryVisibleResidentPursuer(actor,out danger);
            }
            LawfulGuardFlight flight;
            if(!lawfulGuardFlights.TryGetValue(actor.Resident.Id,out flight))return false;
            var guard=flight.Guard;var body=actor.GetComponent<DaggerfallEntityBehaviour>();
            var guardBody=guard ? guard.GetComponent<DaggerfallEntityBehaviour>() : null;
            var residentScene=DefenseScene.For(actor);var guardScene=DefenseScene.For(guardBody);
            bool valid=guard && guard.gameObject.activeInHierarchy && guard.GuardId==flight.GuardId && body &&
                guard.DefenseTarget==body && body.Entity!=null && body.Entity.CurrentHealth>0 && residentScene!=null &&
                guardScene!=null && residentScene.Root==guardScene.Root && residentScene.PlayerPresent &&
                CivilianResponses.Sees(guard.transform.position,actor.transform.position,20);
            if(!valid) {lawfulGuardFlights.Remove(actor.Resident.Id);return false;}
            danger=guard.transform.position;return true;
        }
    }
}
