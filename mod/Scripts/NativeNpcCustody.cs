using System;
using System.Collections.Generic;
using System.Globalization;
using DaggerfallConnect.Utility;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Entity;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    // One concrete surveyed native cell in the currently loaded scene. This is
    // deliberately not a catalog inferred from dungeon names or cage models.
    internal sealed partial class NativeNpcCustodyCell
    {
        internal Transform Root;
        internal DaggerfallActionDoor Door;
        internal Vector3 Inside,Outside;
        internal int Settlement,Building;
        internal string Space;
        internal bool Current {get{return Root && Root.gameObject.activeInHierarchy && Door &&
            Door.gameObject.activeInHierarchy && Door.transform.IsChildOf(Root) && Door.LoadID!=0 &&
            NativeNavigationWorld.Current && NativeNavigationWorld.Current.Ready && NativeNavigationWorld.Current.Scene==Space;}}
        internal NpcActionTarget Position(Vector3 ground)
        {
            var local=Root.InverseTransformPoint(ground);
            return new NpcActionTarget {Kind=Building>=0?NpcActionTargetKind.IndoorPoint:NpcActionTargetKind.WorldPoint,
                SettlementId=Settlement,BuildingKey=Building,WorldSpaceId=Space,X=local.x,Y=local.y,Z=local.z};
        }
        internal Vector3 World(NpcActionTarget position)
        {return Root.TransformPoint(new Vector3((float)position.X,(float)position.Y,(float)position.Z));}
        internal bool Matches(NpcCustodyRecord row)
        {return Current && row.Cell.WorldSpaceId==Space && row.SettlementId==Settlement && row.Cell.BuildingKey==Building &&
            row.CellDoorId==Door.LoadID.ToString(CultureInfo.InvariantCulture) && Vector3.Distance(World(row.Cell),Inside)<.05f;}
        internal bool Path(Vector3 from,Vector3 destination)
        {
            NavMeshHit first,last;var path=new NavMeshPath();
            return Current && NavMesh.SamplePosition(from,out first,.4f,NavMesh.AllAreas) &&
                NavMesh.SamplePosition(destination,out last,.4f,NavMesh.AllAreas) &&
                NavMesh.CalculatePath(first.position,last.position,NavMesh.AllAreas,path) && path.status==NavMeshPathStatus.PathComplete;
        }
    }

    internal static partial class NativeNpcCustody
    {
        internal static NativeNpcCustodyCell Cell {get;private set;}
        // Called only after the concrete jail door and its two standing points
        // have been surveyed. This API cannot manufacture a door or floor.
        internal static bool RegisterSurveyedCell(Transform root,DaggerfallActionDoor door,Vector3 inside,
            Vector3 outside,int settlement,int building)
        {
            var world=NativeNavigationWorld.Current;
            if(!root || !door || !world || !world.Ready || !door.transform.IsChildOf(root) ||
                door.LoadID==0 || !door.IsClosed || !door.IsLocked ||
                Vector3.Distance(inside,outside)<1 || Vector3.Distance(inside,outside)>5)return false;
            RaycastHit barrier;
            if(!Physics.Linecast(inside+Vector3.up,outside+Vector3.up,out barrier,~0,QueryTriggerInteraction.Ignore) ||
                barrier.collider.GetComponentInParent<DaggerfallActionDoor>()!=door)return false;
            foreach(var point in new[]{inside,outside})
            {
                RaycastHit floor;
                if(!Physics.Raycast(point+Vector3.up*.3f,Vector3.down,out floor,.6f,~0,QueryTriggerInteraction.Ignore) ||
                    !floor.transform.IsChildOf(root) || floor.normal.y<.65f)return false;
            }
            var candidate=new NativeNpcCustodyCell {Root=root,Door=door,Inside=inside,Outside=outside,
                Settlement=settlement,Building=building,Space=world.Scene};
            if(!candidate.Path(outside,inside))return false;
            Cell=candidate;return true;
        }
        internal static bool HoldGuard(GuardActor actor,double now)
        {
            return actor && actor.CustodyEscort && actor.CustodyEscort.Tick(now);
        }
        internal static bool HoldsResident(WorldStateStore store,string id)
        {return store!=null && store.HasActiveNpcCustody(id);}
        internal static NativeNpcInventoryBinding LivingOwner(WorldStateStore store,string id)
        {
            // Inactive streamed bodies can still own a live inventory binding.
            // Prefer that exact collection before restoring its persisted owner.
            foreach(var owner in Resources.FindObjectsOfTypeAll<NativeNpcInventoryOwner>())
            {var body=owner.GetComponent<DaggerfallEntityBehaviour>();
                if(owner.IsBoundTo(store) && owner.Binding!=null && owner.Binding.PersonId==id && body && body.Entity!=null && body.Entity.CurrentHealth>0)return owner.Binding;}
            var saved=store==null?null:store.ReadNpcInventory(id);
            if(saved==null)return null;
            NativeNpcInventoryBinding binding;string reason;
            // This exact existing ledger was already created by its real owner.
            // TryOpen restores its native payloads and UIDs; it cannot enter the
            // initialization branch because the record was just required above.
            return NativeNpcInventoryBridge.TryOpen(store,id,null,saved.WalletGold,out binding,out reason)?binding:null;
        }
        internal static DaggerfallLoot ShopStock(string owner)
        {
            var game=GameManager.Instance;var room=game.PlayerEnterExit.Interior;
            if(!game.PlayerEnterExit.IsPlayerInsideBuilding || !room ||
                owner!="native-shop:"+game.PlayerGPS.CurrentMapID+":"+room.EntryDoor.buildingKey)return null;
            foreach(var loot in room.GetComponentsInChildren<DaggerfallLoot>())
                if(loot.LoadID!=0 && loot.ContainerType==LootContainerTypes.ShopShelves && loot.gameObject.activeInHierarchy)return loot;
            return null;
        }
        internal static DaggerfallLoot WallContainer()
        {
            var game=GameManager.Instance;var room=game.PlayerEnterExit.Interior;
            if(!game.PlayerEnterExit.IsPlayerInsideBuilding || !room)return null;
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();BuildingSummary building;
            if(!directory || !directory.GetBuildingSummary(room.EntryDoor.buildingKey,out building) ||
                building.BuildingType!=DaggerfallConnect.DFLocation.BuildingTypes.Town23)return null;
            foreach(var loot in room.GetComponentsInChildren<DaggerfallLoot>())
                if(loot.LoadID!=0 && loot.ContainerType==LootContainerTypes.HouseContainers && loot.gameObject.activeInHierarchy)return loot;
            return null;
        }
    }

    internal sealed partial class NativeNpcCustodySession : MonoBehaviour
    {
        internal GuardActor Guard {get;private set;}
        private WorldStateStore store;
        private NativeNpcCustodyCell cell;
        private ResidentActor actor;
        private string person;
        private int frame=-1;
        private bool handled;
        private bool closing;
        internal bool OwnsLoadedDungeon
        {
            get
            {
                var game=GameManager.Instance;var dungeon=game && game.PlayerEnterExit?game.PlayerEnterExit.Dungeon:null;
                var row=store==null?null:store.ReadNpcCustody(person);
                var owner=actor?actor.GetComponent<NativeNpcInventoryOwner>():null;
                return game && game.PlayerEnterExit.IsPlayerInsideDungeon && dungeon && cell!=null && cell.Root &&
                    cell.Root==dungeon.transform && cell.Door && actor && actor.transform.IsChildOf(cell.Root) &&
                    owner && owner.IsBoundTo(store) && row!=null && row.Phase<NpcCustodyPhase.Released &&
                    row.Cell.WorldSpaceId==cell.Space && row.LastPosition.WorldSpaceId==cell.Space &&
                    row.CellDoorId==cell.Door.LoadID.ToString(CultureInfo.InvariantCulture) &&
                    (row.Phase!=NpcCustodyPhase.Escorting || Guard && Guard.GuardId==row.GuardId && Guard.transform.IsChildOf(cell.Root));
            }
        }
        internal void Bind(WorldStateStore world,ResidentActor prisoner,NativeNpcCustodyCell actualCell,GuardActor escort)
        {FreeGuard();store=world;actor=prisoner;cell=actualCell;person=prisoner.Resident.Id;Guard=escort;
            if(Guard)Guard.CustodyEscort=this;frame=-1;closing=false;}
        internal bool Tick(double now)
        {
            if(frame==Time.frameCount)return handled;
            frame=Time.frameCount;handled=false;
            var row=store==null?null:store.ReadNpcCustody(person);
            if(row==null || row.Phase>=NpcCustodyPhase.Released){FreeGuard();return false;}
            handled=true;
            if(!cell.Matches(row) || !actor || !actor.gameObject.activeInHierarchy || !actor.transform.IsChildOf(cell.Root))return true;
            var body=actor.GetComponent<DaggerfallEntityBehaviour>();
            if(!body || body.Entity==null || body.Entity.CurrentHealth<=0 || !actor.Resident.IsAlive)
            {store.TryCancelNpcCustody(person,row.Revision,now);FreeGuard();return false;}
            if(GameManager.IsGamePaused)return true;
            var actual=actor.transform.position-Vector3.up;
            if(!store.TryObserveNpcCustody(person,row.Revision,cell.Position(actual),now))return true;
            row=store.ReadNpcCustody(person);
            if(row.Phase==NpcCustodyPhase.Released){FreeGuard();return false;}
            if(row.Phase==NpcCustodyPhase.Escorting)
            {
                var guardBody=Guard?Guard.GetComponent<DaggerfallEntityBehaviour>():null;
                var motor=Guard?Guard.GetComponent<EnemyMotor>():null;
                if(!Guard || !guardBody || guardBody.Entity==null || guardBody.Entity.CurrentHealth<=0 || motor && motor.IsHostile)
                {store.TryCancelNpcCustody(person,row.Revision,now);FreeGuard();return false;}
                if(!Guard.gameObject.activeInHierarchy || !Guard.transform.IsChildOf(cell.Root))return true;
                Guard.EndCombat();
                // Keep the stored engagement position equal to this body, so a
                // later interruption returns from the actual escort position.
                if(!Guard.CaptureCustody(actor,now))
                {store.TryCancelNpcCustody(person,row.Revision,now);FreeGuard();return false;}
                if(Vector3.Distance(Guard.GroundPosition,cell.Outside)>.4f)
                {
                    // The existing native navigation component owns both routes.
                    // Stop the escort if the actual prisoner has fallen behind.
                    if(Vector3.Distance(Guard.GroundPosition,actual)<2.5f)MoveGuard(cell.Outside);
                    else HoldGuardBody();
                    var direction=Guard.GroundPosition-actual;
                    if(direction.magnitude>1.2f)MovePrisoner(Guard.GroundPosition-direction.normalized*1.1f);
                    else HoldPrisoner();
                    return true;
                }
                HoldGuardBody();
                if(Vector3.Distance(actual,cell.Inside)>.4f)
                {closing=false;if(!cell.Door.IsOpen && !cell.Door.IsMoving)cell.Door.SetOpen(true,false,true);
                    if(cell.Door.IsOpen)MovePrisoner(cell.Inside);else HoldPrisoner();return true;}
                HoldPrisoner();
                if(!closing){closing=true;cell.Door.SetOpen(false,false,true);return true;}
                if(!cell.Door.IsClosed)return true;
                cell.Door.CurrentLockValue=Math.Max(1,cell.Door.StartingLockValue);
                var draws=new int[NpcNativeSentence.RequiredRandomDraws(row.NativeCrime,0)];
                for(int index=0;index<draws.Length;index++)draws[index]=unchecked((int)DFRandom.rand());
                if(store.TryConfineNpc(person,row.Revision,now,cell.Door.IsClosed,cell.Door.IsLocked,draws))
                {Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NPC_CUSTODY_CONFINED: person="+person+"; actualNativeDoor="+cell.Door.LoadID);FreeGuard();}
                return true;
            }
            FreeGuard();
            if(row.Phase==NpcCustodyPhase.Confined)
            {
                HoldPrisoner();
                if(now>=row.ReleaseAt)store.TryReleaseNpcCustody(person,row.Revision,now);
                return true;
            }
            if(!cell.Door.IsOpen && !cell.Door.IsMoving)cell.Door.SetOpen(true,false,true);
            if(cell.Door.IsOpen)MovePrisoner(cell.World(row.ReleasePoint));else HoldPrisoner();
            return true;
        }
        private void HoldPrisoner()
        {var navigation=actor.GetComponent<NativeResidentNavigation>();if(navigation)navigation.Hold();actor.SetRunningPreview(false);}
        private void MovePrisoner(Vector3 destination)
        {
            actor.ResumeFromSocialPause();
            // A native path refusal falls back only to the current observed
            // ground point, never to the destination across the cell wall.
            actor.Place(actor.transform.position-Vector3.up,destination-actor.transform.position,standing:false,
                waitAtOccupiedTarget:true,navigationDestination:destination);
        }
        private void MoveGuard(Vector3 destination)
        {
            var navigation=Guard.GetComponent<NativeResidentNavigation>()??Guard.gameObject.AddComponent<NativeResidentNavigation>();
            var before=Guard.transform.position;
            if(!navigation.TryMove(Guard.GroundPosition,destination,1.3,null))return;
            Guard.SetInteriorPose(Guard.transform.position,Guard.transform.position-before,(Guard.transform.position-before).sqrMagnitude>.000001f);
        }
        private void HoldGuardBody()
        {var navigation=Guard.GetComponent<NativeResidentNavigation>();if(navigation)navigation.Hold();Guard.SetInteriorPose(Guard.transform.position,Vector3.zero,false);}
        private void FreeGuard()
        {if(!Guard)return;var navigation=Guard.GetComponent<NativeResidentNavigation>();if(navigation)navigation.Pause();
            if(Guard.CustodyEscort==this)Guard.CustodyEscort=null;Guard=null;}
        private void OnDestroy(){FreeGuard();}
    }

    public sealed partial class GuardActor
    {
        internal NativeNpcCustodySession CustodyEscort;
        internal bool CaptureCustody(ResidentActor prisoner,double now)
        {
            var guard=roster.Guards.Find(value=>value.Id==GuardId);
            if(!guard.IsAlive || entity.CurrentHealth<=0 || guard.Duty!=GuardDuty.Engaged ||
                IsInterior && (guard.Interior==null || guard.Interior.Returning) ||
                !IsInterior && guard.Interior!=null)return false;
            bool sees=prisoner && prisoner.gameObject.activeInHierarchy && prisoner.transform.IsChildOf(transform.parent) &&
                CivilianResponses.Sees(transform.position,prisoner.transform.position,20);
            double health=(double)entity.CurrentHealth/entity.MaxHealth;
            if(IsDungeonCustodyBody)
            {
                // The actual custody record owns Dungeon positions. Never
                // overwrite the guard's exterior return anchor with dungeon X/Z.
                if(guard.Interior!=null || guard.EngagementPosition==null)return false;
                roster.ObserveEngagement(GuardId,guard.EngagementPosition,health,now,sees,guard.LastSeenSuspect);
                displayedAt=now;return true;
            }
            var actual=transform.parent.InverseTransformPoint(transform.position);
            var target=sees?transform.parent.InverseTransformPoint(prisoner.transform.position):Vector3.zero;
            if(IsInterior)roster.ObserveInterior(GuardId,Point(actual),health,now,sees,sees?Point(target):null);
            else roster.ObserveEngagement(GuardId,new SavedRoutePoint {X=actual.x,Z=actual.z},health,now,sees,
                sees?new SavedRoutePoint {X=target.x,Z=target.z}:null);
            displayedAt=now;return true;
        }
    }
}
