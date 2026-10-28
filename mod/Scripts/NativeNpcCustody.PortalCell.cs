using System;
using System.Globalization;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    internal sealed partial class NativeNpcCustodyCell
    {
        internal NpcCustodyPortal Portal;
        internal NpcActionTarget SurveyedInside;
        internal string PortalCellDoorId;
        internal WorldStateStore QualifiedStore;
        internal bool KnownExterior(WorldStateStore store)
        {return Portal!=null && ReferenceEquals(store,QualifiedStore) && Portal.Profile==NativeNavigationWorld.Profile() &&
            Portal.Variant==NativeNavigationWorld.Variant();}
    }
    internal static partial class NativeNpcCustody
    {
        internal static string EntranceId(int map,StaticDoor door)
        {return "native-dungeon:"+map+":"+door.blockIndex+":"+door.recordIndex+":"+door.doorIndex;}
        internal static NpcActionTarget SpacePoint(Transform root,Vector3 ground,int map,string space)
        {var p=root.InverseTransformPoint(ground);return new NpcActionTarget {Kind=NpcActionTargetKind.WorldPoint,
            SettlementId=map,WorldSpaceId=space,X=p.x,Y=p.y,Z=p.z};}
        internal static Vector3 SpaceWorld(Transform root,NpcActionTarget point)
        {return root.TransformPoint(new Vector3((float)point.X,(float)point.Y,(float)point.Z));}
        internal static bool Floor(Transform root,Vector3 near,out Vector3 floor)
        {
            RaycastHit hit;floor=Vector3.zero;
            if(!root || !Physics.Raycast(near+Vector3.up*.5f,Vector3.down,out hit,4,~0,QueryTriggerInteraction.Ignore) ||
                !hit.transform.IsChildOf(root) || hit.normal.y<.7f || NativeNavigationGeometry.IsDynamicNavigationBody(hit.collider))return false;
            NavMeshHit sampled;
            if(!NavMesh.SamplePosition(hit.point,out sampled,.4f,NavMesh.AllAreas) || Mathf.Abs(sampled.position.y-hit.point.y)>.4f)return false;
            floor=hit.point;return true;
        }
        internal static bool FindExteriorDoor(NpcCustodyPortal portal,int map,out StaticDoor result,out Transform root)
        {
            result=default(StaticDoor);root=null;var directory=GameManager.Instance.StreamingWorld.GetCurrentBuildingDirectory();
            if(!directory || directory.MapID!=map)return false;
            foreach(var owner in directory.GetComponentsInChildren<DaggerfallStaticDoors>(true))
            foreach(var raw in owner.Doors??new StaticDoor[0])
                if(raw.doorType==DoorTypes.DungeonEntrance && EntranceId(map,raw)==portal.EntranceId)
                {result=raw;result.ownerPosition=owner.transform.position;result.ownerRotation=owner.transform.rotation;root=directory.transform;return true;}
            return false;
        }
        // Caller supplies one identified real cell's actual interior bounds.
        // Current collision, free occupancy, native entrance and complete route
        // are checked here; naming a prison or passing a cage prop is insufficient.
        internal static bool RegisterDungeonCell(DaggerfallDungeon dungeon,DaggerfallActionDoor door,Vector3 inside,
            Vector3 outside,Bounds actualCellBounds,StaticDoor entrance,WorldStateStore store)
        {
            var game=GameManager.Instance;var world=NativeNavigationWorld.Current;
            if(store==null || !game || !game.PlayerEnterExit.IsPlayerInsideDungeon || game.PlayerEnterExit.Dungeon!=dungeon ||
                !dungeon || !dungeon.StartMarker || !world || !world.Ready || world.Scene!="dungeon-"+dungeon.Summary.ID ||
                entrance.doorType!=DoorTypes.DungeonEntrance || !actualCellBounds.Contains(inside+Vector3.up) ||
                actualCellBounds.Contains(outside+Vector3.up) || actualCellBounds.size.x>16 || actualCellBounds.size.z>16 ||
                actualCellBounds.size.y>8 || !ContainedCell(dungeon.transform,actualCellBounds) || !CellFree(actualCellBounds,null))return false;
            Vector3 entry;
            if(!Floor(dungeon.transform,dungeon.StartMarker.transform.position,out entry))return false;
            var candidate=new NativeNpcCustodyCell {Root=dungeon.transform,Door=door,Inside=inside,Outside=outside,
                Settlement=dungeon.Summary.ID,Building=-1,Space=world.Scene};
            if(!candidate.Path(entry,outside))return false;
            var portal=new NpcCustodyPortal {EntranceId=EntranceId(dungeon.Summary.ID,entrance),Profile=NativeNavigationWorld.Profile(),
                Variant=NativeNavigationWorld.Variant(),Entry=candidate.Position(entry),CellOutside=candidate.Position(outside),
                CellMinimum=candidate.Position(actualCellBounds.min),CellMaximum=candidate.Position(actualCellBounds.max)};
            StaticDoor actual;Transform exterior;
            if(!FindExteriorDoor(portal,dungeon.Summary.ID,out actual,out exterior))return false;
            portal.Exterior=SpacePoint(exterior,StreetPopulation.FollowExteriorDoorGround(actual),dungeon.Summary.ID,"city-"+dungeon.Summary.ID);
            portal.GuardPosition=NpcActionExecutor.CopyTarget(portal.Exterior);
            if(!RegisterSurveyedCell(dungeon.transform,door,inside,outside,dungeon.Summary.ID,-1))return false;
            Cell.Portal=portal;Cell.QualifiedStore=store;Cell.SurveyedInside=Cell.Position(inside);
            Cell.PortalCellDoorId=door.LoadID.ToString(CultureInfo.InvariantCulture);return true;
        }
        private static bool ContainedCell(Transform root,Bounds bounds)
        {
            if(bounds.size.x<1 || bounds.size.z<1 || bounds.size.y<1.8f)return false;
            foreach(var direction in new[]{Vector3.right,Vector3.left,Vector3.forward,Vector3.back})
            {
                RaycastHit wall;float extent=Mathf.Abs(direction.x)>0?bounds.extents.x:bounds.extents.z;
                if(!Physics.Raycast(bounds.center,direction,out wall,extent+.4f,~0,QueryTriggerInteraction.Ignore) ||
                    !wall.transform.IsChildOf(root) || wall.normal.y>.65f || wall.distance<extent-.5f)return false;
            }
            return true;
        }
        internal static bool CellFree(Bounds bounds,ResidentActor prisoner)
        {
            foreach(var body in UnityEngine.Object.FindObjectsOfType<DaggerfallEntityBehaviour>())
                if(body.gameObject.activeInHierarchy && (!prisoner || !body.transform.IsChildOf(prisoner.transform)) &&
                    bounds.Contains(body.transform.position) && (body.Entity==null || body.Entity.CurrentHealth>0))return false;
            return true;
        }
        internal static NativeNpcCustodyCell RebindPortalCell(WorldStateStore store,NpcCustodyRecord row,ResidentActor prisoner)
        {
            var game=GameManager.Instance;var dungeon=game.PlayerEnterExit.Dungeon;
            if(row.Portal==null || !game.PlayerEnterExit.IsPlayerInsideDungeon || !dungeon || dungeon.Summary.ID!=row.SettlementId ||
                row.Portal.Profile!=NativeNavigationWorld.Profile() || row.Portal.Variant!=NativeNavigationWorld.Variant())return null;
            if(Cell!=null && Cell.Matches(row) && ReferenceEquals(Cell.QualifiedStore,store))return Cell;
            DaggerfallActionDoor selected=null;
            foreach(var door in dungeon.GetComponentsInChildren<DaggerfallActionDoor>())
                if(door.LoadID.ToString(CultureInfo.InvariantCulture)==row.CellDoorId){selected=door;break;}
            if(!selected)return null;
            var bounds=new Bounds();bounds.SetMinMax(SpaceWorld(dungeon.transform,row.Portal.CellMinimum),SpaceWorld(dungeon.transform,row.Portal.CellMaximum));
            if(!CellFree(bounds,prisoner) || !ContainedCell(dungeon.transform,bounds))return null;
            var candidate=new NativeNpcCustodyCell {Root=dungeon.transform,Door=selected,Inside=SpaceWorld(dungeon.transform,row.Cell),
                Outside=SpaceWorld(dungeon.transform,row.Portal.CellOutside),Settlement=row.SettlementId,Building=-1,Space=row.Cell.WorldSpaceId,
                Portal=row.Portal,QualifiedStore=store,SurveyedInside=row.Cell,PortalCellDoorId=row.CellDoorId};
            Vector3 first,last;
            if(!Floor(dungeon.transform,candidate.Inside,out first) || !Floor(dungeon.transform,candidate.Outside,out last) ||
                Vector3.Distance(first,candidate.Inside)>.4f || Vector3.Distance(last,candidate.Outside)>.4f ||
                !candidate.Path(SpaceWorld(dungeon.transform,row.Portal.Entry),candidate.Outside) || !candidate.Path(candidate.Outside,candidate.Inside))return null;
            Cell=candidate;return candidate;
        }
        internal static NpcCustodyPortal CopyPortal(NpcCustodyPortal value)
        {return value==null?null:new NpcCustodyPortal {EntranceId=value.EntranceId,Profile=value.Profile,Variant=value.Variant,
            Exterior=NpcActionExecutor.CopyTarget(value.Exterior),Entry=NpcActionExecutor.CopyTarget(value.Entry),
            CellOutside=NpcActionExecutor.CopyTarget(value.CellOutside),GuardPosition=NpcActionExecutor.CopyTarget(value.GuardPosition),
            CellMinimum=NpcActionExecutor.CopyTarget(value.CellMinimum),CellMaximum=NpcActionExecutor.CopyTarget(value.CellMaximum),
            Stage=value.Stage,GuardReturned=value.GuardReturned};}
        internal static bool CompletePath(Vector3 from,Vector3 to)
        {
            var world=NativeNavigationWorld.Current;NavMeshHit first,last;var route=new NavMeshPath();
            return world && world.Ready && NavMesh.SamplePosition(from,out first,.4f,NavMesh.AllAreas) &&
                NavMesh.SamplePosition(to,out last,.4f,NavMesh.AllAreas) &&
                NavMesh.CalculatePath(first.position,last.position,NavMesh.AllAreas,route) && route.status==NavMeshPathStatus.PathComplete &&
                world.ClearCityGatePath(route.corners);
        }
        internal static bool ExteriorCellAvailable(NativeNpcCustodyCell cell,WorldStateStore store,Vector3 guard,Vector3 prisoner,out Transform root)
        {
            root=null;StaticDoor door;Vector3 landing;
            if(cell==null || !cell.KnownExterior(store) || cell.SurveyedInside==null ||
                GameManager.Instance.PlayerEnterExit.IsPlayerInside || !FindExteriorDoor(cell.Portal,cell.Settlement,out door,out root) ||
                !Floor(root,StreetPopulation.FollowExteriorDoorGround(door),out landing) ||
                Vector3.Distance(landing,SpaceWorld(root,cell.Portal.Exterior))>.8f)return false;
            return CompletePath(guard,landing) && CompletePath(prisoner,landing);
        }
    }
}
