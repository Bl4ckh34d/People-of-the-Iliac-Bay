using System;
using System.Collections.Generic;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Transient physical response to an observed lawful guard hit. It owns no
    // crime, alarm, threat, custody, persistence or combat state.
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
            ResidentActor actor=FindActor(residentId);Vector3 ignored;
            return actor && TryLawfulGuardFlightDanger(actor,out ignored) ? lawfulGuardFlights[residentId].Reaction : null;
        }

        private bool TryLawfulGuardFlightDanger(ResidentActor actor,out Vector3 danger)
        {
            danger=Vector3.zero;
            if(!actor || actor.Resident==null)return false;
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
