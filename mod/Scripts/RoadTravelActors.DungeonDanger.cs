using System;
using System.Collections.Generic;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Game.Items;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class RoadTravelActors
    {
        // Immediate self-preservation does not create a player Attack order.
        // Native senses must identify the actual currently attacking subject.
        internal static DaggerfallEntityBehaviour CurrentDungeonThreat(ResidentActor actor,float range=6)
        {return NearestDungeonThreat(actor,VisibleDungeonThreats(actor,range),range);}

        private static List<DaggerfallEntityBehaviour> VisibleDungeonThreats(ResidentActor actor,float range)
        {
            var threats=new List<DaggerfallEntityBehaviour>();
            var game=GameManager.Instance;var dungeon=game.PlayerEnterExit.Dungeon;
            if(!actor || !game.PlayerEnterExit.IsPlayerInsideDungeon || !dungeon || !actor.transform.IsChildOf(dungeon.transform))return threats;
            foreach(var candidate in dungeon.GetComponentsInChildren<DaggerfallEntityBehaviour>())
            {
                if(NativeNpcCombatAssist.Identity(candidate)==null || !NativeNpcCombatAssist.ThreatensPlayerOrActor(candidate,actor,false) ||
                    !NativeNpcCombatAssist.SameVisibleScene(actor,candidate,range))continue;
                threats.Add(candidate);
            }
            return threats;
        }
        private static DaggerfallEntityBehaviour NearestDungeonThreat(ResidentActor actor,List<DaggerfallEntityBehaviour> threats,float range)
        {
            DaggerfallEntityBehaviour nearest=null;float distance=range;
            foreach(var candidate in threats)
            {
                float current=Vector3.Distance(actor.transform.position,candidate.transform.position);
                if(current<distance){distance=current;nearest=candidate;}
            }
            return nearest;
        }

        private bool AdvanceDungeonDanger(WorldStateStore store,DungeonFollower binding,NpcActionJob job,double now)
        {
            var actor=binding.Actor;var threats=VisibleDungeonThreats(actor,20);
            var target=NearestDungeonThreat(actor,threats,6);
            // Leaving the immediate six-metre radius does not end a visible
            // attack. Retain the actual known attacker within ordinary civilian
            // sight, otherwise Follow immediately walks back into the danger.
            if(!target && binding.DangerTarget && threats.Contains(binding.DangerTarget))target=binding.DangerTarget;
            if(!target)
            {
                if(binding.DangerId!=null)Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_DANGER_CLEAR: person="+job.ResidentId+
                    "; target="+binding.DangerId+"; originalFollowResumed=true; contractExtended=false");
                binding.DangerId=null;binding.DangerTarget=null;binding.DangerMode=null;binding.DangerRetreating=false;return false;
            }
            binding.DangerTarget=target;
            string targetId=NativeNpcCombatAssist.Identity(target);
            if(binding.DangerId!=targetId){binding.DangerId=targetId;binding.DangerRetreating=false;binding.DangerMode=null;}
            var body=actor.GetComponent<DaggerfallEntityBehaviour>();var senses=target.GetComponent<EnemySenses>();
            bool selfTarget=body && body.Entity!=null && senses && senses.Target==body;
            var role=store.ReadNpcRole(job.ResidentId);
            if(role==null || role.CombatRole==NpcCombatRole.Unknown)
            {PeopleOfTheIliacBayMod.RefreshSavedNpcRole(store,job.ResidentId);role=store.ReadNpcRole(job.ResidentId);}
            int roll;var enemy=target.GetComponent<DaggerfallWorkshop.DaggerfallEnemy>();
            if(body && body.Entity!=null && NativeNpcCombatRoleDecision.TryGetEncounterRoll(store.WorldSeed,0,job.ResidentId,
                enemy?enemy.LoadID:0,targetId,out roll) && NpcRolePolicy.ShouldRetreat(role,body.Entity.CurrentHealth,body.Entity.MaxHealth,roll))
                binding.DangerRetreating=true;
            DaggerfallUnityItem weapon;
            bool fight=selfTarget && NpcRolePolicy.CanDirectCombatAssist(role) && !binding.DangerRetreating &&
                NativeNpcCombatAssist.OwnershipClear(target) && NativeNpcCombatAssist.TryWeapon(actor,out weapon);
            string mode=fight?"self-defense":"flee";
            if(binding.DangerMode!=mode)
            {
                binding.DangerMode=mode;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_DUNGEON_DANGER: person="+job.ResidentId+"; target="+targetId+
                    "; nativeTarget="+(selfTarget?"companion":"player")+"; mode="+mode+
                    "; distance="+Vector3.Distance(actor.transform.position,target.transform.position)+"; originalFollowHeld=true; attackOrderCreated=false");
            }
            var actual=actor.transform.position-Vector3.up;
            if(fight)
            {
                float distance=Vector3.Distance(actor.transform.position,target.transform.position);
                if(distance<=NativeNpcCombatAssist.MeleeRange)
                {
                    var native=actor.GetComponent<NativeResidentNavigation>();if(native)native.Pause();
                    actor.Place(actual,target.transform.position-actor.transform.position,true);
                    if(Time.unscaledTime>=binding.NextDangerSwingAt)
                    {
                        binding.NextDangerSwingAt=Time.unscaledTime+1.5f;int damage;
                        bool swung=NativeNpcCombatAssist.TrySwing(actor,target,store,now,()=>{
                            var current=store.ReadNpcAction(job.Id);var currentSenses=target?target.GetComponent<EnemySenses>():null;
                            return current!=null && !NpcActionExecutor.IsTerminal(current) && now<current.DeadlineAt && now<current.FinishAt &&
                                currentSenses && currentSenses.Target==body && NativeNpcCombatAssist.ThreatensPlayerOrActor(target,actor,false) &&
                                NativeNpcCombatAssist.OwnershipClear(target) && NativeNpcCombatAssist.SameVisibleScene(actor,target,NativeNpcCombatAssist.MeleeRange);
                        },out damage);
                        if(swung)actor.Counterattacks++;
                    }
                }
                else actor.Place(actual,target.transform.position-actor.transform.position,
                    navigationDestination:target.transform.position-Vector3.up);
                return true;
            }
            // FlightSafety already uses twelve metres as its safe-distance
            // envelope. At that distance keep the same body and Follow consent
            // on hold while the known visible native attack is still active.
            if(Vector3.Distance(actor.transform.position,target.transform.position)>=12)
            {
                var stopped=actor.GetComponent<NativeResidentNavigation>();if(stopped)stopped.Pause();
                actor.HoldPhysicalPosition();return true;
            }
            actor.ReleaseFlightPause();
            var away=actual-target.transform.position;away.y=0;if(away.sqrMagnitude<0.001f)away=-actor.transform.forward;
            foreach(float angle in new[]{0f,45f,-45f,90f,-90f})
            {
                var preferred=actual+Quaternion.Euler(0,angle,0)*away.normalized*3;Vector3 safe;
                if(!CivilianFlightSteering.TryDirectRetreat(actual,preferred,out safe) || !SafeDungeonRetreat(actual,safe,threats))continue;
                actor.Place(Vector3.MoveTowards(actual,safe,(float)ResidentMovement.FleeSpeed(actor.Resident.AppearanceSeed)*Time.deltaTime),
                    safe-actual,fleeing:true,placementAllowed:point=>SafeDungeonRetreat(actual,point,threats));return true;
            }
            var navigation=actor.GetComponent<NativeResidentNavigation>();if(navigation)navigation.Pause();
            actor.HoldPhysicalPosition();return true;
        }

        private static bool SafeDungeonRetreat(Vector3 from,Vector3 to,List<DaggerfallEntityBehaviour> threats)
        {
            // A step away from the nearest attacker can approach a second one.
            // Apply the existing safe-distance envelope to every currently seen
            // native attack, including any local crowd-avoidance displacement.
            foreach(var threat in threats)
                if(threat && !CivilianFlightSteering.Safe(from,to,threat.transform.position))return false;
            return true;
        }
    }
}
