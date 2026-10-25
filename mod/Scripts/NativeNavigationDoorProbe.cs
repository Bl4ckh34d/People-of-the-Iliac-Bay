using System;
using System.Collections;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using UnityEngine;

namespace PeopleOfTheIliacBay.DFU
{
    // Explicit native-scene acceptance probe. Never runs during ordinary play.
    internal static class NativeNavigationDoorProbe
    {
        public static IEnumerator Run()
        {
            var game=GameManager.Instance;
            if(!game || !game.PlayerEnterExit || !game.PlayerEnterExit.IsPlayerInside)
                throw new InvalidOperationException("Door probe requires a loaded native interior or dungeon.");
            Transform root=null;string expectedScene=null;
            if(game.PlayerEnterExit.IsPlayerInsideDungeon)
            {var dungeon=game.PlayerEnterExit.Dungeon;if(dungeon){root=dungeon.transform;expectedScene="dungeon-"+dungeon.Summary.ID;}}
            else
            {
                var interior=game.PlayerEnterExit.Interior;
                if(interior && game.PlayerGPS && game.PlayerGPS.HasCurrentLocation)
                {root=interior.transform;expectedScene="interior-"+game.PlayerGPS.CurrentLocation.MapTableData.MapId+"-"+interior.EntryDoor.buildingKey;}
            }
            if(!root)throw new InvalidOperationException("Native door scene root is missing.");
            var world=NativeNavigationWorld.Current;
            float deadline=Time.realtimeSinceStartup+60;
            while((!world || !world.Ready || world.Scene!=expectedScene) && Time.realtimeSinceStartup<deadline)
            {yield return null;world=NativeNavigationWorld.Current;}
            if(!world || !world.Ready || world.Scene!=expectedScene)
                throw new InvalidOperationException("Door probe requires a loaded exact-scene native NavMesh module.");
            if(GameManager.IsGamePaused)
                throw new InvalidOperationException("Door probe requires unpaused native gameplay.");

            DaggerfallActionDoor ordinary=null;
            Vector3 from=Vector3.zero,to=Vector3.zero;
            foreach(var door in root.GetComponentsInChildren<DaggerfallActionDoor>())
            {
                if(!door || !door.isActiveAndEnabled || !door.IsClosed || door.IsLocked ||
                    door.GetComponent<DaggerfallAction>())continue;
                for(int side=-1;side<=1;side+=2)
                {
                    if(!TryApproach(door,side,out from,out to))continue;
                    NativeNpcNavigationDoors.OpenAhead(from,to);
                    float openDeadline=Time.realtimeSinceStartup+2;
                    while(!door.IsOpen && Time.realtimeSinceStartup<openDeadline)yield return null;
                    if(door.IsOpen){ordinary=door;break;}
                }
                if(ordinary)break;
            }
            if(!ordinary)
                throw new InvalidOperationException("No closed, unlocked ordinary native door with a clear approach in this scene.");

            if(!ordinary.IsOpen || ordinary.IsLocked)
                throw new InvalidOperationException("Native NPC approach did not open the ordinary native door.");
            ordinary.SetOpen(false,true);
            float closedDeadline=Time.realtimeSinceStartup+2;
            while(ordinary && !ordinary.IsClosed && Time.realtimeSinceStartup<closedDeadline)yield return null;
            if(!ordinary || !ordinary.IsClosed)
                throw new InvalidOperationException("Native ordinary door was not restored to its closed state.");

            // Controlled native components make both prohibited states deterministic,
            // without changing a quest door or a saved native lock in the scene.
            var lockedObject=MakeIsolatedDoor(root.position+Vector3.up*5000,"Locked native door probe");
            var actionObject=MakeIsolatedDoor(root.position+Vector3.up*5000+Vector3.right*5,"Action native door probe");
            try
            {
                var locked=lockedObject.GetComponent<DaggerfallActionDoor>();
                locked.CurrentLockValue=7;
                var action=actionObject.GetComponent<DaggerfallActionDoor>();
                actionObject.AddComponent<DaggerfallAction>();
                yield return null; // Let native DaggerfallActionDoor.Start establish closed state.
                if(!world || !world.Ready || world.Scene!=expectedScene || GameManager.IsGamePaused)
                    throw new InvalidOperationException("Native navigation scene changed during door probe.");
                AssertClosedAfterApproach(locked,"locked");
                if(locked.CurrentLockValue!=7)
                    throw new InvalidOperationException("Native door approach changed a lock value.");
                AssertClosedAfterApproach(action,"action-owned");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_NAVMESH_DOOR_PASS: scene="+world.Scene+
                    "; nativeOrdinaryOpened=true; lockedClosed=true; actionOwnedClosed=true; restored=true");
            }
            finally
            {
                if(lockedObject)UnityEngine.Object.Destroy(lockedObject);
                if(actionObject)UnityEngine.Object.Destroy(actionObject);
            }
        }

        private static GameObject MakeIsolatedDoor(Vector3 position,string name)
        {
            var obj=new GameObject(name);
            obj.transform.position=position;
            var collider=obj.AddComponent<BoxCollider>();
            collider.center=Vector3.up;collider.size=new Vector3(1,2,0.2f);
            var door=obj.AddComponent<DaggerfallActionDoor>();
            door.PlaySounds=false;
            return obj;
        }

        private static void AssertClosedAfterApproach(DaggerfallActionDoor door,string label)
        {
            Vector3 from,to;
            if(!TryApproach(door,1,out from,out to))
                throw new InvalidOperationException("Probe ray did not contact isolated "+label+" native door.");
            if(!door.IsClosed)throw new InvalidOperationException("Isolated "+label+" native door was not initially closed.");
            NativeNpcNavigationDoors.OpenAhead(from,to);
            if(!door.IsClosed || door.IsOpen)
                throw new InvalidOperationException("Native NPC approach opened "+label+" door.");
        }

        private static bool TryApproach(DaggerfallActionDoor door,int side,out Vector3 from,out Vector3 to)
        {
            from=to=Vector3.zero;
            Collider target=null;
            foreach(var collider in door.GetComponents<Collider>())
                if(collider.enabled && !collider.isTrigger){target=collider;break;}
            if(!target)return false;
            var box=target as BoxCollider;
            Vector3 thin=box && Mathf.Abs(box.size.x*door.transform.lossyScale.x)<
                Mathf.Abs(box.size.z*door.transform.lossyScale.z)?door.transform.right:door.transform.forward;
            thin.y=0;
            if(thin.sqrMagnitude<0.01f)return false;
            thin.Normalize();
            Physics.SyncTransforms();
            var center=target.bounds.center;
            var start=center+thin*(0.8f*side);
            var end=center-thin*(0.8f*side);
            var direction=end-start;
            foreach(var hit in Physics.SphereCastAll(start,0.24f,direction.normalized,
                direction.magnitude+0.24f,~0,QueryTriggerInteraction.Ignore))
                if(hit.collider==target)
                {from=start-Vector3.up;to=end-Vector3.up;return true;}
            return false;
        }
    }
}
