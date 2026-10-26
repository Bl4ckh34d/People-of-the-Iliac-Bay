using System;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    public sealed partial class StreetPopulation
    {
        private sealed class BuildingActionDoor
        {public DaggerfallStaticDoors Owner;public int Index;}
        private TownGeometry buildingActionDoorGeometry;
        private int buildingActionEntranceCount=-1;
        private readonly Dictionary<string,BuildingActionDoor> buildingActionDoors=new Dictionary<string,BuildingActionDoor>();
        private readonly Dictionary<int,BuildingActionDoor> buildingActionEntrances=new Dictionary<int,BuildingActionDoor>();

        private string BuildingActionSpace(int key)
        {return "interior:"+population.SettlementId+":"+key;}
        private string BuildingActionCurrentSpace
        {get{return interior?BuildingActionSpace(interior.EntryDoor.buildingKey):ActionWorldSpace;}}
        private string BuildingActionDoorId(StaticDoor door)
        {return "door:"+population.SettlementId+":"+door.buildingKey+":"+door.blockIndex+":"+door.recordIndex+":"+door.doorIndex;}

        private void ReadBuildingActionDoors()
        {
            if(ReferenceEquals(buildingActionDoorGeometry,geometry) && buildingActionEntranceCount==(geometry==null?0:geometry.Entrances.Count))return;
            buildingActionDoorGeometry=geometry;buildingActionEntranceCount=geometry==null?0:geometry.Entrances.Count;
            buildingActionDoors.Clear();buildingActionEntrances.Clear();
            if(!directory || geometry==null)return;
            foreach(var owner in directory.GetComponentsInChildren<DaggerfallStaticDoors>(true))
            for(int i=0;owner.Doors!=null && i<owner.Doors.Length;i++)
            {
                var door=owner.Doors[i];if(door.doorType!=DoorTypes.Building)continue;
                var source=new BuildingActionDoor {Owner=owner,Index=i};
                buildingActionDoors[BuildingActionDoorId(door)]=source;
                TownGeometry.Entrance entrance;if(!geometry.Entrances.TryGetValue(door.buildingKey,out entrance))continue;
                var normal=owner.GetDoorNormal(i);normal.y=0;
                var point=directory.transform.InverseTransformPoint(owner.GetDoorPosition(i)+normal.normalized*0.6f);
                if(new Vector2(point.x-entrance.LocalDoor.x,point.z-entrance.LocalDoor.z).sqrMagnitude<0.01f)
                    buildingActionEntrances[door.buildingKey]=source;
            }
        }
        private bool ReadBuildingActionDoor(int key,string expectedId,out StaticDoor door)
        {
            door=default(StaticDoor);ReadBuildingActionDoors();BuildingActionDoor source;
            if(expectedId==null?!buildingActionEntrances.TryGetValue(key,out source):!buildingActionDoors.TryGetValue(expectedId,out source))return false;
            if(!source.Owner || source.Index>=source.Owner.Doors.Length)return false;
            door=source.Owner.Doors[source.Index];door.ownerPosition=source.Owner.transform.position;door.ownerRotation=source.Owner.transform.rotation;
            return door.buildingKey==key;
        }
        private bool CanEnterBuilding(ResidentActor actor,int key)
        {
            BuildingSummary building;if(!directory || !directory.GetBuildingSummary(key,out building))return false;
            if(actor.Resident.HomeKey==key)return true;
            var type=building.BuildingType;
            return (RMBLayout.IsShop(type) || type==DFLocation.BuildingTypes.Tavern ||
                type==DFLocation.BuildingTypes.Temple || type==DFLocation.BuildingTypes.GuildHall || type==DFLocation.BuildingTypes.Bank) &&
                (int)type>=0 && (int)type<PlayerActivate.openHours.Length && GameManager.Instance.PlayerActivate &&
                GameManager.Instance.PlayerActivate.BuildingIsUnlocked(building);
        }
        private bool TryBuildingActionTarget(ResidentActor actor,NpcActionKind kind,int key,string expectedDoorId,out NpcActionTarget target)
        {
            target=null;StaticDoor door;
            if(kind==NpcActionKind.Enter)
            {
                if(interior || !CanEnterBuilding(actor,key) || !ReadBuildingActionDoor(key,expectedDoorId,out door))return false;
            }
            else
            {
                if(kind!=NpcActionKind.Exit || !interior || interior.EntryDoor.buildingKey!=key ||
                    indoorGeometry==null || !indoorGeometry.EntryGround.HasValue)return false;
                door=interior.EntryDoor;
            }
            target=new NpcActionTarget {Kind=NpcActionTargetKind.Building,SettlementId=population.SettlementId,BuildingKey=key,
                DoorId=BuildingActionDoorId(door),WorldSpaceId=BuildingActionCurrentSpace,
                DestinationSpaceId=kind==NpcActionKind.Enter?BuildingActionSpace(key):ActionWorldSpace};
            return true;
        }
        private void OfferBuildingEntry(ResidentActor actor,List<NpcActionOption> options,int key,string name,string text,bool semantic)
        {
            NpcActionTarget target;
            if(TryBuildingActionTarget(actor,NpcActionKind.Enter,key,null,out target))
                AddActionOption(options,NpcActionKind.Enter,target,"enter "+name+" through its actual door (no purchase or other service)",text,semantic);
        }
        private void OfferBuildingExit(ResidentActor actor,List<NpcActionOption> options,string text,bool semantic)
        {
            NpcActionTarget target;
            if(interior && TryBuildingActionTarget(actor,NpcActionKind.Exit,interior.EntryDoor.buildingKey,null,out target))
                AddActionOption(options,NpcActionKind.Exit,target,"leave this building through its actual entrance",text,semantic);
        }
        private bool ResolveBuildingAction(NpcActionJob job,ResidentActor actor,out Vector3 target,out string reason)
        {
            target=Vector3.zero;reason="That exact native doorway is not available or accessible now.";
            NpcActionTarget current;
            if(!actor || !TryBuildingActionTarget(actor,job.Kind,job.Target.BuildingKey,job.Target.DoorId,out current) ||
                !NpcTaskContract.TargetMatches(job.Target,current))return false;
            if(job.Kind==NpcActionKind.Exit)target=indoorGeometry.EntryGround.Value;
            else
            {
                // The building's default entrance can change after this action
                // was accepted. Resolve the same physical door as the receipt.
                StaticDoor door;
                if(geometry==null || !ReadBuildingActionDoor(job.Target.BuildingKey,job.Target.DoorId,out door))return false;
                var ground=FollowExteriorDoorGround(door);
                var local=directory.transform.InverseTransformPoint(ground);
                target=geometry.ScenePoint(new RoutePoint(local.x,local.z));
                if(Vector3.Distance(ground,target)>0.4f)return false;
            }
            reason=null;return true;
        }
        private bool ExecuteBuildingAction(NpcActionJob job,ref ResidentActor actor,double now,out string reason)
        {
            Vector3 doorway;
            if(!ResolveBuildingAction(job,actor,out doorway,out reason) ||
                Vector3.Distance(actor.transform.position-Vector3.up,doorway)>0.6f)return false;
            var receipt=NpcBuildingTransitionReceipt.Create(job,actor.Resident.Id,population.SettlementId,
                job.Target.BuildingKey,job.Target.DoorId,BuildingActionCurrentSpace,job.Target.DestinationSpaceId,now);
            if(!worldStore.TryCompleteNpcBuildingTransition(job.Id,job.Revision,now,receipt))
            {reason="The native doorway action changed before arrival.";return false;}
            job=worldStore.ReadNpcAction(job.Id);
            actor.CaptureState();RememberActionOutcome(job,now);NotifyActionOutcome(job,actor,null);
            // Keep the observed source position until the destination scene supplies its real landing.
            // The existing typed receipt records which side of this exact door now owns the NPC.
            if(!worldStore.SaveNpcActionPosition(job.Id,job.Revision,ActionPosition(actor.transform.position-Vector3.up),true,now))return false;
            StaticDoor actualDoor;
            if(job.Kind==NpcActionKind.Enter && ReadBuildingActionDoor(job.Target.BuildingKey,job.Target.DoorId,out actualDoor))
            {
                var guards=GetComponent<GuardPopulation>();
                if(guards)guards.ObserveResidentBuildingEntry(actor,actualDoor,now);
            }
            string id=job.ResidentId;actor.gameObject.SetActive(false);Destroy(actor.gameObject);actors.Remove(id);actor=null;
            actionMotions.Remove(id);RemovePhysicalShopVisit(id);worldStore.RemoveObservedVisit(id);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_BUILDING_ACTION_CROSSED: action="+job.Id+"; kind="+job.Kind+"; door="+receipt.DoorId);
            reason=null;return true;
        }
        private bool BeginUnplannedBuildingReturn(ResidentActor actor,NpcActionJob job,double now,out string reason)
        {
            reason="The actual building exit is not ready for routine return.";
            if(!actor || !interior || indoorGeometry==null || !indoorGeometry.EntryGround.HasValue || geometry==null ||
                job==null || !job.ReturningToRoutine || actor.Resident.Id!=job.ResidentId)return false;
            Vector3 actual=actor.transform.position-Vector3.up;
            if(Vector3.Distance(actual,indoorGeometry.EntryGround.Value)>0.6f)return false;
            StaticDoor door;
            if(!ReadBuildingActionDoor(interior.EntryDoor.buildingKey,BuildingActionDoorId(interior.EntryDoor),out door))return false;
            Vector3 outside=FollowExteriorDoorGround(door),local=directory.transform.InverseTransformPoint(outside);
            var landing=geometry.ScenePoint(new RoutePoint(local.x,local.z));
            if(Vector3.Distance(outside,landing)>0.4f)return false;
            var destination=new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,SettlementId=population.SettlementId,
                WorldSpaceId=ActionWorldSpace,BuildingKey=-1,X=local.x,Y=local.y,Z=local.z};
            var source=ActionPosition(actual);
            if(!worldStore.SaveNpcActionPosition(job.Id,job.Revision,source,true,now))return false;
            job=worldStore.ReadNpcAction(job.Id);
            var route=new List<NpcActionTarget> {source,ActionPosition(indoorGeometry.EntryGround.Value)};
            if(!worldStore.TryBeginNpcRoutineRecoveryTransition(job.Id,job.Revision,now,destination,route,
                DaggerfallUnity.Instance.WorldTime.TimeScale/actor.WalkingSpeed,interior.EntryDoor.buildingKey))return false;
            actor.CaptureState();string id=job.ResidentId;
            RemovePhysicalShopVisit(id);worldStore.RemoveObservedVisit(id);actionMotions.Remove(id);
            actor.gameObject.SetActive(false);Destroy(actor.gameObject);actors.Remove(id);
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_ROUTINE_UNPLANNED_EXIT: resident="+id+
                "; building="+interior.EntryDoor.buildingKey+"; actualDoor="+BuildingActionDoorId(door));
            reason=null;return true;
        }

        private bool HoldBuildingActionLanding(ref NpcActionJob job,double now)
        {
            var receipt=job.BuildingTransitionReceipt;
            if(receipt==null || receipt.DestinationObserved || !job.ReturningToRoutine || job.LastPosition==null)return false;
            string lastSpace=job.LastPosition.Kind==NpcActionTargetKind.IndoorPoint?BuildingActionSpace(job.LastPosition.BuildingKey):job.LastPosition.WorldSpaceId;
            if(lastSpace==receipt.DestinationSpaceId)return false;
            // Release unseen visits to ordinary schedules after the agreed window.
            // A departed body must remain absent while its source room is watched.
            if(BuildingActionCurrentSpace!=receipt.DestinationSpaceId && BuildingActionCurrentSpace!=receipt.SourceSpaceId &&
                now>=job.DeadlineAt &&
                worldStore.ActiveNpcTask(job.ResidentId)==null)
            {
                if(worldStore.SaveNpcActionPosition(job.Id,job.Revision,job.LastPosition,false,now))
                {job=worldStore.ReadNpcAction(job.Id);return false;}
                return true;
            }
            if(GameManager.IsGamePaused)return true;
            if(BuildingActionCurrentSpace!=receipt.DestinationSpaceId)
            {
                // The existing per-plan handoff owns this clock check. A known
                // public-building Wait may elapse after the real door crossing
                // while the player stays outside; no interior is loaded or
                // positioned until the player actually enters it.
                var task=worldStore.ActiveNpcTask(job.ResidentId);
                if(task!=null)worldStore.TryCompleteOffscreenBuildingWait(task.Id,task.Revision,now);
                return true;
            }
            Vector3 landing;
            if(job.Kind==NpcActionKind.Enter)
            {
                if(!interior || BuildingActionDoorId(interior.EntryDoor)!=receipt.DoorId ||
                    indoorGeometry==null || !indoorGeometry.EntryGround.HasValue)return true;
                landing=indoorGeometry.EntryGround.Value;
            }
            else
            {
                StaticDoor door;if(interior || !ReadBuildingActionDoor(receipt.BuildingKey,receipt.DoorId,out door))return true;
                var point=directory.transform.InverseTransformPoint(FollowExteriorDoorGround(door));
                landing=geometry.ScenePoint(new RoutePoint(point.x,point.z));
            }
            if(!FollowLandingClear(landing))return true;
            if(!worldStore.SaveNpcActionPosition(job.Id,job.Revision,ActionPosition(landing),true,now))return true;
            job=worldStore.ReadNpcAction(job.Id);
            if(job.Kind==NpcActionKind.Enter)
            {
                // A deliberate return can resume an unfinished visit which the
                // previous Exit's derived continuation had skipped.
                string residentId=job.ResidentId;
                var plan=plans.Find(item=>item.Resident.Id==residentId);
                bool restricted=population.SettlementId==1291010263 && worldStore.RestrictHauntingRoutines(
                    population.SettlementId,GameManager.Instance.PlayerEntity.GlobalVars.GetGlobalVar(0),now);
                double start,end;
                var ordinary=plan==null?null:AppointmentSchedule(plan,restricted?plan.HauntedSchedule:plan.Schedule,restricted);
                if(ordinary!=null && RuntimeFamily(plan,now)==null && ordinary.TryVisitWindowForBuilding(now,receipt.BuildingKey,out start,out end) &&
                    start<=now && now<end)
                {worldStore.RemoveRoutineRecovery(job.ResidentId);InvalidateRoutineRecovery(job.ResidentId);}
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_BUILDING_ACTION_LANDED: action="+job.Id+"; kind="+job.Kind+"; door="+receipt.DoorId);
            return false;
        }
    }
}
