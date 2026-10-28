using System;
using System.Collections;
using System.Collections.Generic;
using DaggerfallConnect;
using DaggerfallConnect.Arena2;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using DaggerfallWorkshop.Game.Items;
using DaggerfallWorkshop.Utility;
using PeopleOfTheIliacBay.Core;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    // Diagnostic only. A room name, door or floor anchor is never admitted as a cell.
    internal static class NativeCustodyGeometryProbe
    {
        internal static IEnumerator RunCastle()
        {
            var game=GameManager.Instance;
            if(!game || !NativeProbeText.IsTestCharacter(game.PlayerEntity.Name) || game.PlayerEnterExit.IsPlayerInside)
                throw new InvalidOperationException("Castle custody geometry capture requires an owned exterior fixture.");
            bool tristane=Environment.GetEnvironmentVariable("POTIB_CUSTODY_TRISTANE")=="1";
            if(tristane)yield return StageTristane();
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            if(!directory || !directory.LocationData.HasDungeon)
                throw new InvalidOperationException("This actual town has no native connected dungeon.");
            DaggerfallStaticDoors selected=null;StaticDoor entry=default(StaticDoor);
            foreach(var owner in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
            foreach(var door in owner.Doors??new StaticDoor[0])
                if(door.doorType==DoorTypes.DungeonEntrance)
                {selected=owner;entry=door;break;}
            if(!selected)throw new InvalidOperationException("Actual town has no loaded dungeon entrance door.");
            int map=directory.MapID;var location=directory.LocationData;
            game.PlayerEnterExit.TransitionDungeonInterior(selected.transform,entry,location);
            float deadline=Time.realtimeSinceStartup+30;DaggerfallDungeon dungeon=null;
            while(Time.realtimeSinceStartup<deadline)
            {
                dungeon=game.PlayerEnterExit.Dungeon;
                if(game.PlayerEnterExit.IsPlayerInsideDungeon && dungeon && dungeon.StartMarker)break;
                yield return null;
            }
            if(!dungeon || !game.PlayerEnterExit.IsPlayerInsideDungeon || dungeon.Summary.ID!=map)
                throw new InvalidOperationException("Actual connected town dungeon did not load with matching map identity.");
            DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);yield return null;
            string capture=Environment.GetEnvironmentVariable("POTIB_CUSTODY_NAV_CAPTURE");
            if(!string.IsNullOrWhiteSpace(capture))
            {
                // This scene already has a native RDB placement manifest. Capture
                // its loaded blocks with that format; a whole-scene replacement
                // would correctly violate the manifest placement contract.
                yield return NativeDungeonNavigationCatalog.ExportLoaded(capture);
                Debug.Log((tristane?"PEOPLE_OF_THE_ILIAC_BAY_PRISON_CUSTODY_NAV_CAPTURE_PASS:":
                    "PEOPLE_OF_THE_ILIAC_BAY_CASTLE_CUSTODY_NAV_CAPTURE_PASS:")+" map="+map+
                    (tristane?"; actualLoadedPrisonScene=true":"; actualLoadedCastleScene=true")+
                    "; nativeRdbModules=true; cellAdmitted=false");
                if(!tristane)yield break;
            }
            var start=dungeon.StartMarker.GetComponentInParent<DaggerfallRDBBlock>();
            if(tristane && (!start || start.name!="DaggerfallBlock [W0000002.RDB]"))
                throw new InvalidOperationException("The actual Tristane start block differs from the exact native source candidate.");
            var doors=dungeon.GetComponentsInChildren<DaggerfallActionDoor>(true);
            var loot=dungeon.GetComponentsInChildren<DaggerfallLoot>(true);
            int locked=0;foreach(var door in doors)if(door.IsLocked)locked++;
            string expectedScene="dungeon-"+map;
            deadline=Time.realtimeSinceStartup+30;
            while(Time.realtimeSinceStartup<deadline && !(NativeNavigationWorld.Current &&
                NativeNavigationWorld.Current.Ready && NativeNavigationWorld.Current.Scene==expectedScene))yield return null;
            bool navigation=NativeNavigationWorld.Current && NativeNavigationWorld.Current.Ready &&
                NativeNavigationWorld.Current.Scene==expectedScene;
            if(!navigation)Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CASTLE_CUSTODY_NAVIGATION_UNAVAILABLE: expected="+expectedScene+
                "; actual="+(NativeNavigationWorld.Current?NativeNavigationWorld.Current.Scene:"none")+
                "; ready="+(NativeNavigationWorld.Current && NativeNavigationWorld.Current.Ready)+"; pathsNotQueried=true");
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CASTLE_CUSTODY_GEOMETRY_SOURCE: map="+map+
                "; location="+dungeon.Summary.LocationName+"; connectedNativeEntrance=true; blocks="+
                dungeon.GetComponentsInChildren<DaggerfallRDBBlock>().Length+"; actionDoors="+doors.Length+
                "; currentlyLocked="+locked+"; lootContainers="+loot.Length+"; nativeNavigationReady="+navigation+
                "; startBlock="+(start?start.name:"missing"));
            int sampled=0,bothFloors=0,completeFromEntrance=0;
            Vector3 origin;bool originFloor=DungeonFloor(dungeon.transform,tristane?dungeon.StartMarker.transform.position:
                game.PlayerMotor.transform.position,out origin);
            if(tristane)Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TRISTANE_START_SOURCE: actualStartMarker="+
                dungeon.transform.InverseTransformPoint(dungeon.StartMarker.transform.position)+
                "; supportedOrigin="+originFloor+"; nativeNavigationReady="+navigation+"; noNpcTransport=true; cellAdmitted=false");
            foreach(var door in doors)
            {
                if(!start || !door.transform.IsChildOf(start.transform))continue;
                if(tristane && Array.IndexOf(new ulong[]{27783597,27783539,27783451,27783403,27783355,27783307},door.LoadID)<0)continue;
                var box=door.GetComponent<BoxCollider>();var center=box?box.bounds.center:door.transform.position;
                Vector3 front,back;bool first=DungeonFloor(dungeon.transform,center+door.transform.forward*1.2f,out front);
                bool second=DungeonFloor(dungeon.transform,center-door.transform.forward*1.2f,out back);
                if(first && second)bothFloors++;
                bool routeFront=originFloor && first && navigation && DungeonPath(origin,front);
                bool routeBack=originFloor && second && navigation && DungeonPath(origin,back);
                if(tristane)
                {
                    RaycastHit barrier;
                    bool physicalDoor=first && second && Physics.Linecast(front+Vector3.up,back+Vector3.up,
                        out barrier,~0,QueryTriggerInteraction.Ignore) && barrier.collider.GetComponentInParent<DaggerfallActionDoor>()==door;
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TRISTANE_CELL_DOOR_SOURCE: load="+door.LoadID+
                        "; actualClosed="+door.IsClosed+"; actualLocked="+door.IsLocked+"; physicalDoorBarrier="+physicalDoor+
                        "; frontFloor="+first+"; backFloor="+second+"; pathQueried="+navigation+
                        "; startPathFront="+routeFront+"; startPathBack="+routeBack+
                        "; crossingPath="+(first && second && navigation && DungeonPath(front,back))+
                        "; containmentRequiresCapturedGeometry=true; cellAdmitted=false");
                }
                if(routeFront || routeBack)completeFromEntrance++;
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CASTLE_CUSTODY_GEOMETRY_DOOR: load="+door.LoadID+
                    "; block="+start.name+"; local="+start.transform.InverseTransformPoint(center)+
                    "; startingLock="+door.StartingLockValue+"; currentLock="+door.CurrentLockValue+"; open="+door.IsOpen+
                    "; frontFloor="+first+"; backFloor="+second+"; entrancePathFront="+routeFront+"; entrancePathBack="+routeBack+
                    "; frontDungeonLocal="+dungeon.transform.InverseTransformPoint(front)+
                    "; backDungeonLocal="+dungeon.transform.InverseTransformPoint(back)+
                    "; cellAdmitted=false");
                var nearby=new List<MeshFilter>();
                foreach(var mesh in start.GetComponentsInChildren<MeshFilter>(true))
                {
                    if(!mesh.sharedMesh || mesh.GetComponentInParent<DaggerfallActionDoor>())continue;
                    var point=mesh.transform.TransformPoint(mesh.sharedMesh.bounds.center);
                    if((point-center).sqrMagnitude<=25)nearby.Add(mesh);
                }
                nearby.Sort((a,b)=>(a.transform.TransformPoint(a.sharedMesh.bounds.center)-center).sqrMagnitude.CompareTo(
                    (b.transform.TransformPoint(b.sharedMesh.bounds.center)-center).sqrMagnitude));
                for(int index=0;index<Math.Min(6,nearby.Count);index++)
                {
                    var mesh=nearby[index];
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CASTLE_CUSTODY_GEOMETRY_NEARBY_MESH: door="+door.LoadID+
                        "; object="+mesh.name+"; mesh="+mesh.sharedMesh.name+
                        "; local="+start.transform.InverseTransformPoint(mesh.transform.TransformPoint(mesh.sharedMesh.bounds.center))+
                        "; cellAdmitted=false");
                }
                sampled++;yield return null;
            }
            if(tristane)
            {
                if(sampled!=6)throw new InvalidOperationException("Tristane's exact native six-door cluster was not present: "+sampled);
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TRISTANE_CUSTODY_SOURCE_CAPTURED: map="+map+
                    "; exactDoors="+sampled+"; supportedSides="+bothFloors+"; nativeEntrancePathTargets="+completeFromEntrance+
                    "; pathsQueried="+navigation+"; cellAdmitted=false; custodyAccepted=false; noNpcTransport=true");
                yield break;
            }
            foreach(var placed in dungeon.Summary.LocationData.Dungeon.Blocks)
            {
                DFBlock source;
                if(!DaggerfallUnity.Instance.ContentReader.GetBlock(placed.BlockName,out source))continue;
                foreach(var group in source.RdbBlock.ObjectRootList)
                {
                    if(group.RdbObjects==null)continue;
                    foreach(var item in group.RdbObjects)
                    {
                        if(item.Type!=DFBlock.RdbResourceTypes.Model)continue;
                        uint model=source.RdbBlock.ModelReferenceList[item.Resources.ModelResource.ModelIndex].ModelIdNum;
                        // Exact native editor-catalog IDs: cages, stocks and torture furniture.
                        // Their presence is evidence of props, never of a usable custody cell.
                        if(model!=41312 && model!=41313 && model!=41700 && model!=41701 &&
                            model!=41300 && model!=41301 && model!=41302 && model!=41303)continue;
                        var local=new Vector3(placed.X*RDBLayout.RDBSide,0,placed.Z*RDBLayout.RDBSide)+
                            new Vector3(item.XPos,-item.YPos,item.ZPos)*MeshReader.GlobalScale;
                        Vector3 floor;bool supported=DungeonFloor(dungeon.transform,dungeon.transform.TransformPoint(local),out floor);
                        bool path=originFloor && supported && navigation && DungeonPath(origin,floor);
                        Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CASTLE_CUSTODY_GEOMETRY_PROP: block="+placed.BlockName+
                            "; nativeModel="+model+"; dungeonLocal="+local+"; actualFloor="+supported+
                            "; entrancePath="+path+"; sourceRecord=true; cellAdmitted=false");
                        if(model==41313 && string.Equals(placed.BlockName,"W0000016.RDB",StringComparison.OrdinalIgnoreCase))
                            DescribeKnownCastleCage(dungeon,doors,local,origin,originFloor,navigation);
                        yield return null;
                    }
                }
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CASTLE_CUSTODY_GEOMETRY_CAPTURED: map="+map+
                "; startBlockDoorsSampled="+sampled+"; bothSideFloors="+bothFloors+
                "; entrancePathTargets="+completeFromEntrance+"; cellAdmitted=false; requiresActualCellContainmentAndFreeOccupancy=true");
        }

        private static IEnumerator StageTristane()
        {
            const int map=1199779128;
            var game=GameManager.Instance;
            var streets=UnityEngine.Object.FindObjectOfType<StreetPopulation>();
            var field=typeof(StreetPopulation).GetField("worldStore",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            var store=streets?field.GetValue(streets) as WorldStateStore:null;
            if(store==null || store.PendingNpcActions().Count!=0 || store.PendingNpcTasks().Count!=0)
                throw new InvalidOperationException("Tristane source capture requires the clean owned checkpoint with no active NPC action or task to transport.");
            DFLocation location;
            if(!DaggerfallUnity.Instance.ContentReader.GetLocation("Daggerfall","The Dungeon of Tristane",out location) ||
                !location.Loaded || !location.HasDungeon || location.MapTableData.MapId!=map ||
                location.MapTableData.DungeonType!=DFRegion.DungeonTypes.Prison)
                throw new InvalidOperationException("The exact native Tristane Prison location is unavailable.");
            var pixel=MapsFile.LongitudeLatitudeToMapPixel(location.MapTableData.Longitude,location.MapTableData.Latitude);
            game.StreamingWorld.TeleportToCoordinates(pixel.X,pixel.Y,StreamingWorld.RepositionMethods.RandomStartMarker);
            float deadline=Time.realtimeSinceStartup+30;
            while(Time.realtimeSinceStartup<deadline)
            {
                var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
                if(game.StreamingWorld.IsReady && !game.StreamingWorld.IsRepositioningPlayer && !game.IsPlayerInside &&
                    directory && directory.MapID==map && game.PlayerGPS.HasCurrentLocation &&
                    game.PlayerGPS.CurrentLocation.MapTableData.MapId==map)
                {
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_TRISTANE_CONTROLLED_SETUP: map="+map+
                        "; nativeLocationTransport=true; playerOnly=true; prisonerOrGuardTransport=false; sourceTravelMeasured=false");
                    yield break;
                }
                yield return null;
            }
            throw new InvalidOperationException("Actual Tristane exterior did not become ready within the bounded native transport setup.");
        }

        private static void DescribeKnownCastleCage(DaggerfallDungeon dungeon,DaggerfallActionDoor[] doors,
            Vector3 cageLocal,Vector3 entrance,bool entranceFloor,bool navigation)
        {
            var cage=dungeon.transform.TransformPoint(cageLocal);DaggerfallActionDoor selected=null;float nearest=float.MaxValue;
            foreach(var door in doors)
            {
                var block=door.GetComponentInParent<DaggerfallRDBBlock>();
                if(!block || !string.Equals(block.name,"DaggerfallBlock [W0000016.RDB]",StringComparison.OrdinalIgnoreCase))continue;
                var box=door.GetComponent<BoxCollider>();var center=box?box.bounds.center:door.transform.position;
                float distance=Vector3.Distance(cage+Vector3.up,center);
                if(distance<nearest){nearest=distance;selected=door;}
            }
            if(!selected)
            {Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CASTLE_CUSTODY_KNOWN_CAGE: block=W0000016.RDB; nativeModel=41313; actualNativeDoor=false; cellAdmitted=false");return;}
            var collider=selected.GetComponent<BoxCollider>();var doorway=collider?collider.bounds.center:selected.transform.position;
            Vector3 first,second;bool front=DungeonFloor(dungeon.transform,doorway+selected.transform.forward*1.2f,out first);
            bool back=DungeonFloor(dungeon.transform,doorway-selected.transform.forward*1.2f,out second);
            RaycastHit barrier;bool actualBarrier=front && back && Physics.Linecast(first+Vector3.up,second+Vector3.up,
                out barrier,~0,QueryTriggerInteraction.Ignore) && barrier.collider.GetComponentInParent<DaggerfallActionDoor>()==selected;
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CASTLE_CUSTODY_KNOWN_CAGE: block=W0000016.RDB; nativeModel=41313; cageDungeonLocal="+cageLocal+
                "; nativeDoor="+selected.LoadID+"; cageToDoorMetres="+nearest+"; doorDungeonLocal="+dungeon.transform.InverseTransformPoint(doorway)+
                "; startingLock="+selected.StartingLockValue+"; currentLock="+selected.CurrentLockValue+"; closed="+selected.IsClosed+
                "; frontFloor="+front+"; backFloor="+back+"; frontDungeonLocal="+dungeon.transform.InverseTransformPoint(first)+
                "; backDungeonLocal="+dungeon.transform.InverseTransformPoint(second)+"; actualDoorBarrier="+actualBarrier+
                "; frontEntrancePath="+(navigation && entranceFloor && front && DungeonPath(entrance,first))+
                "; backEntrancePath="+(navigation && entranceFloor && back && DungeonPath(entrance,second))+
                "; doorCrossingPath="+(navigation && front && back && DungeonPath(first,second))+"; cellAdmitted=false");
        }

        private static bool DungeonFloor(Transform root,Vector3 near,out Vector3 floor)
        {
            floor=Vector3.zero;RaycastHit hit;
            if(!Physics.Raycast(near+Vector3.up*0.4f,Vector3.down,out hit,4,~0,QueryTriggerInteraction.Ignore) ||
                !hit.collider.transform.IsChildOf(root) || hit.normal.y<0.7f ||
                NativeNavigationGeometry.IsDynamicNavigationBody(hit.collider))return false;
            floor=hit.point;return true;
        }
        private static bool DungeonPath(Vector3 from,Vector3 to)
        {
            NavMeshHit start,end;
            if(!NavMesh.SamplePosition(from,out start,0.4f,NavMesh.AllAreas) ||
                !NavMesh.SamplePosition(to,out end,0.4f,NavMesh.AllAreas))return false;
            var path=new NavMeshPath();
            return NavMesh.CalculatePath(start.position,end.position,NavMesh.AllAreas,path) && path.status==NavMeshPathStatus.PathComplete;
        }

        internal static IEnumerator Run()
        {
            var game=GameManager.Instance;
            if(!game || !NativeProbeText.IsTestCharacter(game.PlayerEntity.Name) || game.PlayerEnterExit.IsPlayerInside)
                throw new InvalidOperationException("Custody geometry capture requires an owned exterior fixture.");
            var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            if(!directory)throw new InvalidOperationException("Custody geometry capture has no real town directory.");
            DaggerfallStaticDoors selected=null;StaticDoor entry=default(StaticDoor);
            // Installed Detailed City Walls puts its multi-room basement under
            // WALLAA00 record 0. The source asset is a candidate, never cell proof.
            foreach(var owner in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
            foreach(var door in owner.Doors??new StaticDoor[0])
            {
                if(door.doorType!=DoorTypes.Building || door.blockIndex!=675 || door.recordIndex!=0)continue;
                if(!selected || door.buildingKey<entry.buildingKey){selected=owner;entry=door;}
            }
            if(!selected)throw new InvalidOperationException("No actual WALLAA00 basement entrance exists in this town.");
            TownSmokeHarness.EnterInterior(selected.transform,entry);
            DaggerfallInterior room=null;float deadline=Time.realtimeSinceStartup+30;
            while(Time.realtimeSinceStartup<deadline)
            {
                room=game.PlayerEnterExit.Interior;
                if(game.PlayerEnterExit.IsPlayerInsideBuilding && room && room.EntryDoor.buildingKey==entry.buildingKey)break;
                yield return null;
            }
            if(!room || room.EntryDoor.buildingKey!=entry.buildingKey)
                throw new InvalidOperationException("Actual wall basement did not load.");
            DaggerfallUI.Instance.PopToHUD();game.PauseGame(false);
            DFBlock block;
            if(!NativeInteriorNavigationModules.TryGetBlock(room,out block))
                throw new InvalidOperationException("Loaded wall basement has no exact native interior record.");
            var record=block.RmbBlock.SubRecords[room.EntryDoor.recordIndex].Interior;
            var doors=room.GetComponentsInChildren<DaggerfallActionDoor>(true);
            var loot=room.GetComponentsInChildren<DaggerfallLoot>(true);
            var models=new Dictionary<uint,int>();
            foreach(var model in record.Block3dObjectRecords)
            {int count;models.TryGetValue(model.ModelIdNum,out count);models[model.ModelIdNum]=count+1;}
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CUSTODY_GEOMETRY_SOURCE: map="+directory.MapID+"; building="+entry.buildingKey+
                "; block="+block.Name+"; record="+entry.recordIndex+"; models="+record.Block3dObjectRecords.Length+
                "; nativeDoorRecords="+record.BlockDoorRecords.Length+"; actualActionDoors="+doors.Length+
                "; actualContainers="+loot.Length+"; identity="+NativeInteriorNavigationModules.InteriorKey(block,entry.recordIndex));
            foreach(var model in models)
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CUSTODY_GEOMETRY_MODEL: id="+model.Key+"; count="+model.Value);
            foreach(var door in doors)
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CUSTODY_GEOMETRY_DOOR: load="+door.LoadID+
                    "; local="+room.transform.InverseTransformPoint(door.transform.position)+"; open="+door.IsOpen+
                    "; startingLock="+door.StartingLockValue+"; currentLock="+door.CurrentLockValue);
            foreach(var container in loot)
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CUSTODY_GEOMETRY_CONTAINER: load="+container.LoadID+
                    "; type="+container.ContainerType+"; local="+room.transform.InverseTransformPoint(container.transform.position)+
                    "; items="+(container.Items==null?-1:container.Items.Count));
            var geometry=new InteriorGeometry(room,deferSurvey:true);
            while(geometry.AdvanceSurvey())yield return null;
            int reachable=0,sampled=0;
            int stride=Math.Max(1,geometry.LocalAnchors.Count/12);
            for(int index=0;index<geometry.LocalAnchors.Count && sampled<12;index+=stride)
            {
                var path=geometry.RouteToExit(room.transform.TransformPoint(geometry.LocalAnchors[index]));
                sampled++;
                if(path.Count>=2)reachable++;
                yield return null;
            }
            Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_CUSTODY_GEOMETRY_CAPTURED: building="+entry.buildingKey+
                "; entryFloor="+geometry.EntryGround.HasValue+"; surveyedStandingPoints="+geometry.LocalAnchors.Count+
                "; routeSamples="+sampled+"; reachableSamples="+reachable+
                "; cellAdmitted=false; requiresRealContainmentAndFreeOccupancy=true");
        }
    }
}
