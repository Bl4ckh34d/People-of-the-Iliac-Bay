using System.Collections;
using DaggerfallWorkshop;
using DaggerfallWorkshop.Game;
using UnityEngine;
using UnityEngine.AI;

namespace PeopleOfTheIliacBay.DFU
{
    internal static partial class NativeDungeonFollowProbe
    {
        internal static IEnumerator StageActualFastTravelEntrance(ResidentActor actor,int map)
        {
            var game=GameManager.Instance;var directory=game.StreamingWorld.GetCurrentBuildingDirectory();
            Need(actor && directory && directory.MapID==map && game.PlayerGPS.CurrentLocation.HasDungeon,
                "Dungeon entrance staging requires the actual completed fast-travel destination and original body.");
            Vector3 selected=Vector3.zero;bool found=false;float nearest=100;
            foreach(var collection in directory.GetComponentsInChildren<DaggerfallStaticDoors>())
                foreach(var raw in collection.Doors??new StaticDoor[0])
                {
                    if(raw.doorType!=DoorTypes.DungeonEntrance)continue;
                    var door=raw;door.ownerPosition=collection.transform.position;door.ownerRotation=collection.transform.rotation;
                    var normal=DaggerfallStaticDoors.GetDoorNormal(door);normal.y=0;
                    Vector3 floor;var expected=StreetPopulation.FollowExteriorDoorGround(door)+normal.normalized*0.8f;
                    if(!StaticFloor(expected,null,out floor) || !BodyClear(floor,floor,game.PlayerMotor.transform,false))continue;
                    float distance=Vector3.Distance(actor.transform.position-Vector3.up,floor);
                    if(distance>=nearest)continue;nearest=distance;selected=floor;found=true;
                }
            Need(found,"No actual native dungeon entrance has a clear supported observer approach within 100 metres.");
            var controller=game.PlayerMotor.GetComponent<CharacterController>();Need(controller && controller.enabled,"Native observer controller is unavailable.");
            bool oldMotor=game.PlayerMotor.enabled;game.PlayerMotor.enabled=false;
            try
            {
                DaggerfallUI.Instance.PopToHUD();controller.enabled=false;game.PlayerMotor.transform.position=selected+Vector3.up;
                controller.enabled=true;Physics.SyncTransforms();
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAST_TRAVEL_DUNGEON_ENTRANCE_SETUP: map="+map+
                    "; controlledObserverPlacement=true; npcPlaced=false; approachDistance="+nearest);
                float deadline=Time.realtimeSinceStartup+60;
                var previous=actor.transform.position;float walked=0,maximumStep=0;
                while(Time.realtimeSinceStartup<deadline && actor && Vector3.Distance(actor.transform.position-Vector3.up,selected)>3)
                {
                    yield return null;
                    if(!actor)break;
                    float step=Vector3.Distance(previous,actor.transform.position);walked+=step;maximumStep=Mathf.Max(maximumStep,step);
                    Need(step<=0.55f,"Dungeon entrance approach moved the actual follower by an unphysical step.");
                    previous=actor.transform.position;
                }
                if(actor && Vector3.Distance(actor.transform.position-Vector3.up,selected)>3)
                {
                    var actual=actor.transform.position-Vector3.up;NavMeshHit from,to;var path=new NavMeshPath();
                    bool start=NavMesh.SamplePosition(actual,out from,1,NavMesh.AllAreas);
                    bool end=NavMesh.SamplePosition(selected,out to,1,NavMesh.AllAreas);
                    bool calculated=start && end && NavMesh.CalculatePath(from.position,to.position,NavMesh.AllAreas,path);
                    Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAST_TRAVEL_DUNGEON_APPROACH_BLOCKED: actual="+actual+"; target="+selected+
                        "; remaining="+Vector3.Distance(actual,selected)+"; nativeReady="+(NativeNavigationWorld.Current && NativeNavigationWorld.Current.Ready)+
                        "; sampledStart="+start+"; sampledTarget="+end+"; calculated="+calculated+"; pathStatus="+path.status+"; corners="+path.corners.Length);
                }
                Need(actor && Vector3.Distance(actor.transform.position-Vector3.up,selected)<=3,
                    "Actual fast-travel follower did not physically approach the native dungeon entrance.");
                Debug.Log("PEOPLE_OF_THE_ILIAC_BAY_FAST_TRAVEL_DUNGEON_APPROACH_PASS: walked="+walked+"; maximumStep="+maximumStep+
                    "; npcPlaced=false; remaining="+Vector3.Distance(actor.transform.position-Vector3.up,selected));
                DaggerfallStaticDoors owner;StaticDoor entrance;
                Need(FindEntrance(map,out owner,out entrance),"The observer did not reach a real native dungeon entrance.");
            }
            finally{game.PlayerMotor.enabled=oldMotor;}
        }
    }
}
