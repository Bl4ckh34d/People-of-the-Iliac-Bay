using System;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class GuardPopulation
    {
        private string PursuitDoorId(StaticDoor door)
        { return "door:"+directory.MapID+":"+door.buildingKey+":"+door.blockIndex+":"+door.recordIndex+":"+door.doorIndex; }

        // Called while the resident still has its actual exterior body, only
        // after the resident's own doorway transition has committed.
        public void ObserveResidentBuildingEntry(ResidentActor resident,StaticDoor door,double now)
        {
            try { ObserveResidentBuildingEntryCore(resident,door,now); }
            catch(Exception error)
            {
                // Optional eyewitness bookkeeping cannot strand a committed
                // resident transition or prevent the source body being removed.
                Debug.LogWarning("PEOPLE_OF_THE_ILIAC_BAY_GUARD_DOOR_DECLINED: "+error.GetType().Name);
            }
        }

        private void ObserveResidentBuildingEntryCore(ResidentActor resident,StaticDoor door,double now)
        {
            if(!OwnsExterior || !EnemyDefense || GameManager.IsGamePaused || !resident || resident.Resident==null ||
                !resident.Resident.IsAlive || resident.Resident.IsQuestProtected || !resident.gameObject.activeInHierarchy ||
                door.doorType!=DoorTypes.Building || geometry==null)return;
            var target=resident.GetComponent<DaggerfallEntityBehaviour>();
            if(!target || target.Entity==null || target.Entity.CurrentHealth<=0 || target.Entity.IsInvisible ||
                target.Entity.Team==MobileTeams.PlayerAlly)return;
            Vector3 doorway=Vector3.zero;bool found=false;
            foreach(var owner in directory.GetComponentsInChildren<DaggerfallStaticDoors>(true))
            {
                for(int i=0;owner.Doors!=null && i<owner.Doors.Length;i++)
                {
                    var candidate=owner.Doors[i];
                    if(candidate.doorType!=DoorTypes.Building || PursuitDoorId(candidate)!=PursuitDoorId(door))continue;
                    candidate.ownerPosition=owner.transform.position;candidate.ownerRotation=owner.transform.rotation;
                    doorway=StreetPopulation.FollowExteriorDoorGround(candidate);found=true;break;
                }
                if(found)break;
            }
            if(!found || Vector3.Distance(resident.transform.position-Vector3.up,doorway)>0.65f)return;
            string identity=ThreatKey(target);var destination=Local(doorway);
            int followed=0;
            foreach(var guard in Roster.Guards)
            {
                if(followed>=GuardRoster.MaximumCorpseSearchGuards)break;
                GuardActor actor;
                if(!guard.IsAlive || guard.Interior!=null || guard.Refuge!=null || guard.DoorPursuit!=null ||
                    !actors.TryGetValue(guard.Id,out actor) || !actor || !actor.gameObject.activeInHierarchy || actor.IsInterior ||
                    actor.DefenseTarget!=target || !SeesDoorTarget(actor,target))continue;
                var report=Roster.Reports.Find(item=>item.Id==guard.IncidentId);
                if(report==null || !(report.ThreatId==identity || report.CorpseSuspect!=null &&
                    report.CorpseSuspect.Kind==CorpseSuspectKind.HostileEntity && report.CorpseSuspect.Identity==identity))continue;
                actor.Capture(now);
                var path=geometry.RouteBetweenPoints(Local(actor.transform.position),destination);
                var home=geometry.RouteBetweenPoints(destination,guard.Post);
                if(path.Count<2 || home.Count<2)continue;
                if(!Roster.PursueWitnessedDoor(guard.Id,identity,door.buildingKey,PursuitDoorId(door),now,true,path,home,
                    2.8,DaggerfallUnity.Instance.WorldTime.TimeScale))continue;
                actor.EndCombat();followed++;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_GUARD_WITNESSED_DOOR: guard="+guard.Id+"; target="+identity+"; door="+PursuitDoorId(door));
            }
        }

        internal static bool SeesDoorTarget(GuardActor actor,DaggerfallEntityBehaviour target)
        {
            if(!actor || !target || !target.gameObject.activeInHierarchy || target.Entity==null ||
                target.Entity.CurrentHealth<=0 || target.Entity.IsInvisible)return false;
            var senses=actor.GetComponent<EnemySenses>();
            if(!senses || Vector3.Angle(actor.transform.forward,target.transform.position-actor.transform.position)>=senses.FieldOfView*0.5f)return false;
            var sourceScene=DefenseScene.For(actor);var targetScene=DefenseScene.For(target);
            return sourceScene!=null && targetScene!=null && sourceScene.Root==targetScene.Root &&
                CivilianResponses.Sees(actor.transform.position,target.transform.position,Mathf.Min(20,senses.SightRadius));
        }

        private bool UpdateWitnessedDoorApproach(GuardRecord guard,GuardActor actor,double now)
        {
            if(guard.DoorPursuit==null || guard.Duty==GuardDuty.Returning || guard.Duty==GuardDuty.Posted)return false;
            actor.EndCombat();actor.Place(now);
            // The exterior route owns travel; crossing the streamed seam does
            // not invent an unloaded interior landing or target position.
            if(guard.Duty==GuardDuty.Searching)actor.gameObject.SetActive(false);
            return true;
        }

        public bool TryVisibleResidentPursuer(ResidentActor resident,out Vector3 position)
        {
            position=Vector3.zero;
            if(!OwnsTown() || !resident)return false;
            var target=resident.GetComponent<DaggerfallEntityBehaviour>();
            foreach(var actor in actors.Values)
            {
                if(!actor || !actor.gameObject.activeInHierarchy || !target || actor.DefenseTarget!=target)continue;
                var body=actor.GetComponent<DaggerfallEntityBehaviour>();
                var observerScene=DefenseScene.For(resident);var guardScene=DefenseScene.For(actor);
                if(body && body.Entity!=null && body.Entity.CurrentHealth>0 && !body.Entity.IsInvisible &&
                    observerScene!=null && guardScene!=null && observerScene.Root==guardScene.Root &&
                    StreetPopulation.SeesSceneEvent(resident,actor.transform.position,20))
                {position=actor.transform.position;return true;}
            }
            return false;
        }

        private bool TryEnterWitnessedDoor(GuardRecord guard,double now)
        {
            if(guard.DoorPursuit==null || !entryGround.HasValue)return false;
            var point=interior.transform.InverseTransformPoint(entryGround.Value+Vector3.up);
            return Roster.EnterWitnessedDoor(guard.Id,interior.EntryDoor.buildingKey,PursuitDoorId(interior.EntryDoor),now,
                new GuardPoint3 {X=point.x,Y=point.y,Z=point.z});
        }

        private bool UpdateWitnessedDoorInterior(GuardRecord guard,GuardActor actor,double now)
        {
            if(guard.DoorPursuit==null)return false;
            if(actor.DefenseTarget && actor.GetComponent<EnemyMotor>().IsHostile)
            {DetachWitnessedDoorForPlayerAssault(guard,actor,now);return true;}
            actor.Capture(now);
            if(!EnemyDefense || now>=guard.SearchUntil)
            { actor.EndCombat();Roster.EndInteriorSearch(guard.Id,now);actor.Place(now);return true; }
            DaggerfallEntityBehaviour target=null;
            // The loaded room is the only target source. Identity grants no
            // coordinates; actual sight is still required to start combat.
            foreach(var candidate in interior.GetComponentsInChildren<DaggerfallEntityBehaviour>())
                if(IsReacquirableMurderer(candidate) && ThreatKey(candidate)==guard.DoorPursuit.ThreatId)
                {target=candidate;break;}
            if((!target || actor.DefenseTarget!=target) && (actor.CombatReady || actor.DefenseTarget))actor.EndCombat();
            if(target && SeesDoorTarget(actor,target))actor.BeginDefenseCombat(target,now);
            else if(!actor.CombatReady && guard.Interior.LastSeenSuspect!=null)actor.SearchInterior(interior,now);
            return true;
        }

        private void DetachWitnessedDoorForPlayerAssault(GuardRecord guard,GuardActor actor,double now)
        {
            string incident="door-assault:"+guard.Id+":"+now.ToString("R",System.Globalization.CultureInfo.InvariantCulture);
            Roster.ReassignDoorAssault(guard.Id,incident,now);
            var game=GameManager.Instance;
            if(game.PlayerEntity.CrimeCommitted==PlayerEntity.Crimes.None)game.PlayerEntity.CrimeCommitted=PlayerEntity.Crimes.Assault;
            if(guard.Interior==null)guard.LastSeenSuspect=Local(game.PlayerMotor.transform.position);
            actor.EndCombat();actor.BeginCombat(now);
        }
    }

    public sealed partial class StreetPopulation
    {
        private void ObserveResidentShelterEntry(ResidentActor actor,AlarmPerson reaction,double now)
        {
            StaticDoor door;
            if(!actor || reaction==null || !reaction.ReachedRefuge ||
                !ReadBuildingActionDoor(reaction.BuildingKey,null,out door))return;
            RememberLawfulGuardShelterEntry(actor,door,now);
            var guards=GetComponent<GuardPopulation>();
            if(guards)guards.ObserveResidentBuildingEntry(actor,door,now);
        }
    }
}
