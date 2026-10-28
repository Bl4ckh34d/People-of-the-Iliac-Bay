using System;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        public Action<Exception> BeginPlayerResidentAttack(ResidentActor victim,DaggerfallEntityBehaviour source)
        {return source==GameManager.Instance.PlayerEntityBehaviour?BeginMobileResidentAttack(victim,source):null;}

        public Action<Exception> BeginMobileResidentAttack(ResidentActor victim,DaggerfallEntityBehaviour source)
        {
            var game=GameManager.Instance;
            var body=victim?victim.GetComponent<DaggerfallEntityBehaviour>():null;
            if(!Ready || !ReactionsEnabled || worldStore==null || !victim || !body || body.Entity==null ||
                !victim.Resident.IsAlive || victim.Resident.IsQuestProtected || !source || source.Entity==null ||
                FindActor(victim.Resident.Id)!=victim)return null;
            if(source==body)return null;
            if(source.GetComponent<GuardActor>())return BeginLawfulGuardResidentAttack(victim,source);
            bool playerSource=source==game.PlayerEntityBehaviour;
            int before=body.Entity.CurrentHealth;var expected=worldStore;
            return error=>
            {
                if(error!=null || !victim || !body || expected!=worldStore || !victim.Resident.IsAlive ||
                    body.Entity==null || body.Entity.CurrentHealth<=0 || body.Entity.CurrentHealth>=before)return;
                var clock=DaggerfallUnity.Instance.WorldTime.Now;
                double now=clock.ToSeconds()+(clock.Second-Math.Floor(clock.Second));
                var point=victim.transform.parent.InverseTransformPoint(victim.transform.position-Vector3.up);
                int building=interior?interior.EntryDoor.buildingKey:-1;
                AlarmPerson response;
                if(playerSource)
                    response=worldStore.RecordPlayerAssaultResponse(population.SettlementId,victim.Resident.Id,now,
                        building,point.x,point.y,point.z);
                else
                {
                    if(!source)return;
                    var evidence=source.GetComponent<GuardAttackEvidence>();
                    if(!evidence)evidence=source.gameObject.AddComponent<GuardAttackEvidence>();
                    evidence.Until=now+900;
                    var attackingResident=source.GetComponent<ResidentActor>();
                    if(attackingResident && source.Entity is CivilianEntity)
                    {
                        var scene=DefenseScene.For(source);if(scene==null)return;
                        evidence.ResidentAssault=true;evidence.SceneRoot=scene.Root;evidence.Victim=body;
                    }
                    var guards=GetComponent<GuardPopulation>();
                    string threat=guards?guards.RegisterHostileThreat(source):null;
                    if(threat==null)return;
                    var alarm=worldStore.RecordHostileThreat(population.SettlementId,threat,now);
                    var danger=victim.transform.parent.InverseTransformPoint(source.transform.position-Vector3.up);
                    if(!alarm.HasLocation)worldStore.LocateViolence(alarm.Id,building,danger.x,danger.y,danger.z);
                    response=worldStore.Witness(alarm.Id,victim.Resident.Id,now);
                    if(response!=null)
                    {response.HasPosition=true;response.BuildingKey=building;response.X=point.x;response.Y=point.y;response.Z=point.z;}
                }
                if(response!=null && response.Reaction==CivilianReaction.Flee)
                    InterruptResidentForFlight(victim.Resident.Id,victim,now);
                PrepareIndoorEscapes(now);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_MOBILE_ASSAULT_RESPONSE: resident="+victim.Resident.Id+
                    "; health="+before+"->"+body.Entity.CurrentHealth+"; reaction="+(response==null?"none":response.Reaction.ToString())+
                    "; actualPlayerDamage="+playerSource+"; nativeDamageSource="+(source?source.name:"gone")+
                    "; livingVictim=true; murder=false");
            };
        }

        private void InterruptResidentForFlight(string id,ResidentActor actor,double now)
        {
            if(actor)
            {
                actor.ReleaseFlightPause();
                var host=GetComponent<PeopleOfTheIliacBayMod>();
                if(host)host.InterruptResidentInteractionForFlight(actor);
            }
            var job=worldStore.ActiveNpcAction(id);
            if(job!=null)EndAction(job,now,"Immediate flight interrupted this action.",false);
            ReleaseActionSeat(id);actionMotions.Remove(id);medicalMotions.Remove(id);
            CancelCashForCare(id);ClearProactiveContactMotion(id);
        }
    }
}
